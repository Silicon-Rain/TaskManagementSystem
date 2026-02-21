using Shared.Enums;

namespace API.Data.Entities;

public class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime OccuredOn { get; set; } = DateTime.UtcNow;
    public EventType Type { get; set; } = EventType.Unknown;
    public string Data { get; set; } = string.Empty;
    public DateTime? ProcessedOn { get; set; }
}