using System.ComponentModel.DataAnnotations;

namespace TodoApi.DTOs
{
  public class ForgotPasswordRequest
  {
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;
  }
}
