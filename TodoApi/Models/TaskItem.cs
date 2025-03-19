using System.ComponentModel.DataAnnotations;

namespace TodoApi.Models
{
  public class TaskItem
  {
    public int Id { get; set; }

    [Required]
    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public DateTime? DueDate { get; set; }

    public PriorityLevel Priority { get; set; } = PriorityLevel.Normal;

    public bool IsCompleted { get; set; } = false;

    // Foreign key: the user who owns the task
    public int UserId { get; set; }
    public User? User { get; set; }
  }
}
