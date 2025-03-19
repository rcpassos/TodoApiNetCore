using System.ComponentModel.DataAnnotations;

namespace TodoApi.DTOs
{
  public class SubscribeRequest
  {
    [Required]
    public string PriceId { get; set; } = string.Empty;
  }
}
