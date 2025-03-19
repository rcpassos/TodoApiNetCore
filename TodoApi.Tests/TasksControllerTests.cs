using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TodoApi.Controllers;
using TodoApi.Data;
using TodoApi.DTOs;
using TodoApi.Models;
using Xunit;

namespace TodoApi.Tests
{
  public class TasksControllerTests
  {
    // Creates a fresh in-memory database for each test
    private AppDbContext GetDbContext()
    {
      var options = new DbContextOptionsBuilder<AppDbContext>()
          .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
          .Options;
      return new AppDbContext(options);
    }

    // Sets up the Controller's HttpContext with a user claim for NameIdentifier
    private void SetUser(ControllerBase controller, int userId)
    {
      var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString())
            };
      var identity = new ClaimsIdentity(claims, "Test");
      controller.ControllerContext = new ControllerContext
      {
        HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
      };
    }

    [Fact]
    public async Task CreateTask_ReturnsCreatedTask()
    {
      // Arrange
      var context = GetDbContext();
      var controller = new TasksController(context);
      SetUser(controller, 1); // Simulate user with ID 1

      var dto = new CreateTaskDto
      {
        Title = "Test Task",
        Description = "Test Description",
        DueDate = DateTime.UtcNow.AddDays(1),
        Priority = PriorityLevel.High
      };

      // Act
      var result = await controller.CreateTask(dto);

      // Assert
      var createdResult = Assert.IsType<CreatedAtActionResult>(result);
      var taskItem = Assert.IsType<TaskItem>(createdResult.Value);
      Assert.Equal("Test Task", taskItem.Title);
      Assert.Equal("Test Description", taskItem.Description);
      Assert.Equal(PriorityLevel.High, taskItem.Priority);
      Assert.Equal(1, taskItem.UserId);
    }

    [Fact]
    public async Task GetMyTasks_ReturnsOnlyUserTasks()
    {
      // Arrange
      var context = GetDbContext();
      // Insert tasks for user 1 and user 2
      context.Tasks.Add(new TaskItem { Title = "Task 1", UserId = 1 });
      context.Tasks.Add(new TaskItem { Title = "Task 2", UserId = 1 });
      context.Tasks.Add(new TaskItem { Title = "Task 3", UserId = 2 });
      await context.SaveChangesAsync();

      var controller = new TasksController(context);
      SetUser(controller, 1);

      // Act
      var result = await controller.GetMyTasks();

      // Assert
      var okResult = Assert.IsType<OkObjectResult>(result);
      var tasks = Assert.IsType<List<TaskItem>>(okResult.Value);
      Assert.Equal(2, tasks.Count);
      foreach (var task in tasks)
      {
        Assert.Equal(1, task.UserId);
      }
    }

    [Fact]
    public async Task GetTask_ReturnsTask_WhenExistsAndBelongsToUser()
    {
      // Arrange
      var context = GetDbContext();
      var taskItem = new TaskItem { Title = "Task 1", UserId = 1 };
      context.Tasks.Add(taskItem);
      await context.SaveChangesAsync();

      var controller = new TasksController(context);
      SetUser(controller, 1);

      // Act
      var result = await controller.GetTask(taskItem.Id);

      // Assert
      var okResult = Assert.IsType<OkObjectResult>(result);
      var returnedTask = Assert.IsType<TaskItem>(okResult.Value);
      Assert.Equal(taskItem.Id, returnedTask.Id);
    }

    [Fact]
    public async Task GetTask_ReturnsNotFound_WhenTaskDoesNotBelongToUser()
    {
      // Arrange
      var context = GetDbContext();
      var taskItem = new TaskItem { Title = "Task 1", UserId = 2 };
      context.Tasks.Add(taskItem);
      await context.SaveChangesAsync();

      var controller = new TasksController(context);
      SetUser(controller, 1); // Simulate user 1, but task belongs to user 2

      // Act
      var result = await controller.GetTask(taskItem.Id);

      // Assert
      Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task UpdateTask_ReturnsUpdatedTask_WhenSuccessful()
    {
      // Arrange
      var context = GetDbContext();
      var taskItem = new TaskItem
      {
        Title = "Old Title",
        Description = "Old Description",
        UserId = 1,
        Priority = PriorityLevel.Normal,
        IsCompleted = false
      };
      context.Tasks.Add(taskItem);
      await context.SaveChangesAsync();

      var controller = new TasksController(context);
      SetUser(controller, 1);

      var updateDto = new UpdateTaskDto
      {
        Title = "New Title",
        Description = "New Description",
        DueDate = DateTime.UtcNow.AddDays(2),
        Priority = PriorityLevel.High,
        IsCompleted = true
      };

      // Act
      var result = await controller.UpdateTask(taskItem.Id, updateDto);

      // Assert
      var okResult = Assert.IsType<OkObjectResult>(result);
      var updatedTask = Assert.IsType<TaskItem>(okResult.Value);
      Assert.Equal("New Title", updatedTask.Title);
      Assert.Equal("New Description", updatedTask.Description);
      Assert.Equal(PriorityLevel.High, updatedTask.Priority);
      Assert.True(updatedTask.IsCompleted);
    }

    [Fact]
    public async Task UpdateTask_ReturnsNotFound_WhenTaskDoesNotBelongToUser()
    {
      // Arrange
      var context = GetDbContext();
      var taskItem = new TaskItem
      {
        Title = "Task 1",
        UserId = 2
      };
      context.Tasks.Add(taskItem);
      await context.SaveChangesAsync();

      var controller = new TasksController(context);
      SetUser(controller, 1); // Simulate user 1, but task belongs to user 2

      var updateDto = new UpdateTaskDto
      {
        Title = "Updated Title",
        Description = "Updated Description",
        DueDate = DateTime.UtcNow.AddDays(2),
        Priority = PriorityLevel.Low,
        IsCompleted = false
      };

      // Act
      var result = await controller.UpdateTask(taskItem.Id, updateDto);

      // Assert
      Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task DeleteTask_ReturnsNoContent_WhenSuccessful()
    {
      // Arrange
      var context = GetDbContext();
      var taskItem = new TaskItem
      {
        Title = "Task to delete",
        UserId = 1
      };
      context.Tasks.Add(taskItem);
      await context.SaveChangesAsync();

      var controller = new TasksController(context);
      SetUser(controller, 1);

      // Act
      var result = await controller.DeleteTask(taskItem.Id);

      // Assert
      Assert.IsType<NoContentResult>(result);
      // Verify that the task is removed
      var deletedTask = await context.Tasks.FindAsync(taskItem.Id);
      Assert.Null(deletedTask);
    }

    [Fact]
    public async Task DeleteTask_ReturnsNotFound_WhenTaskDoesNotBelongToUser()
    {
      // Arrange
      var context = GetDbContext();
      var taskItem = new TaskItem
      {
        Title = "Task to delete",
        UserId = 2
      };
      context.Tasks.Add(taskItem);
      await context.SaveChangesAsync();

      var controller = new TasksController(context);
      SetUser(controller, 1);

      // Act
      var result = await controller.DeleteTask(taskItem.Id);

      // Assert
      Assert.IsType<NotFoundResult>(result);
    }
  }
}
