namespace TodoApi.Models
{
    public class WebhookEvent
    {
        public int Id { get; set; }
        public string EventId { get; set; } = string.Empty;
        public DateTime ProcessedAt { get; set; }
    }
}