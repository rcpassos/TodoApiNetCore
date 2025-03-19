using System.ComponentModel.DataAnnotations;
using TodoApi.Models;

namespace TodoApi.DTOs
{
  public class CreateTaskDto
  {
    [Required]
    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
    public DateTime? DueDate { get; set; }
    public PriorityLevel Priority { get; set; } = PriorityLevel.Normal;
  }
}
