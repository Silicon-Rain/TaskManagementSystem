using Shared.Enums;

namespace Shared.Messages;

public class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AggregateId { get; set; }
    public DateTime OccuredOn { get; set; } = DateTime.UtcNow;
    public EventType Type { get; set; } = EventType.Unknown;
    public string Data { get; set; } = string.Empty;
    public DateTime? ProcessedOn { get; set; }
}