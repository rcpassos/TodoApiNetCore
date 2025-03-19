using System.ComponentModel.DataAnnotations;

namespace TodoApi.Models
{
  public class User
  {
    public int Id { get; set; }

    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    // Role can be "User" or "Admin"
    public string Role { get; set; } = "User";

    // Fields for password recovery
    public string? PasswordResetToken { get; set; }
    public DateTime? ResetTokenExpires { get; set; }

    // For Stripe integration (stores Stripe Customer ID)
    public string? StripeCustomerId { get; set; }
  }
}
