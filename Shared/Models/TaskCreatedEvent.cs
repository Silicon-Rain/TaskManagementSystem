namespace Shared.Models;

public class TaskCreatedEvent
{
    public Guid TaskId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
}