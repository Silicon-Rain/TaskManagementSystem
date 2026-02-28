using Shared.Enums;

namespace Shared.Messages;

public class InboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AggregateId { get; set; }
    public EventType Type { get; set; } = EventType.Unknown;
    public string Data { get; set; } = string.Empty;
    public DateTime ReceivedOn { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedOn { get; set; }
}