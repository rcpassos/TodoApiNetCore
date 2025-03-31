namespace TodoApi.DTOs
{
  public class UserResponse
  {
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? StripeCustomerId { get; set; }
    public string? StripeSubscriptionId { get; set; }
    public string? SubscriptionStatus { get; set; }
    public DateTime? SubscriptionEndDate { get; set; }
  }
}
