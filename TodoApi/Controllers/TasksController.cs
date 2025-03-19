using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TodoApi.Data;
using TodoApi.DTOs;
using TodoApi.Models;

namespace TodoApi.Controllers
{
  [Authorize]
  [ApiController]
  [Route("api/[controller]")]
  public class TasksController : ControllerBase
  {
    private readonly AppDbContext _context;
    public TasksController(AppDbContext context)
    {
      _context = context;
    }

    // Create a new task
    [HttpPost]
    public async Task<IActionResult> CreateTask(CreateTaskDto dto)
    {
      var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
      var taskItem = new TaskItem
      {
        Title = dto.Title,
        Description = dto.Description,
        DueDate = dto.DueDate,
        Priority = dto.Priority,
        UserId = userId
      };
      _context.Tasks.Add(taskItem);
      await _context.SaveChangesAsync();
      return CreatedAtAction(nameof(GetTask), new { id = taskItem.Id }, taskItem);
    }

    // Get tasks for the current user
    [HttpGet]
    public async Task<IActionResult> GetMyTasks()
    {
      var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
      var tasks = await _context.Tasks.Where(t => t.UserId == userId).ToListAsync();
      return Ok(tasks);
    }

    // Get a single task by ID (only if it belongs to the user)
    [HttpGet("{id}")]
    public async Task<IActionResult> GetTask(int id)
    {
      var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
      var taskItem = await _context.Tasks.FindAsync(id);
      if (taskItem == null || taskItem.UserId != userId)
        return NotFound();
      return Ok(taskItem);
    }

    // Update a task
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateTask(int id, UpdateTaskDto dto)
    {
      var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
      var taskItem = await _context.Tasks.FindAsync(id);
      if (taskItem == null || taskItem.UserId != userId)
        return NotFound();

      taskItem.Title = dto.Title;
      taskItem.Description = dto.Description;
      taskItem.DueDate = dto.DueDate;
      taskItem.Priority = dto.Priority;
      taskItem.IsCompleted = dto.IsCompleted;
      await _context.SaveChangesAsync();
      return Ok(taskItem);
    }

    // Delete a task
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteTask(int id)
    {
      var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
      var taskItem = await _context.Tasks.FindAsync(id);
      if (taskItem == null || taskItem.UserId != userId)
        return NotFound();

      _context.Tasks.Remove(taskItem);
      await _context.SaveChangesAsync();
      return NoContent();
    }
  }
}
