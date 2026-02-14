namespace Worker.Models;

public class InboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime OccuredOn { get; set; } = DateTime.UtcNow;
    public string Type { get; set; } = string.Empty;
    public string Data { get; set; } = string.Empty;
    public DateTime ProcessedOn { get; set; }
}