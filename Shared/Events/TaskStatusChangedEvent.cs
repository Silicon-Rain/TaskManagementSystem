using TaskStatus = Shared.Enums.TaskStatus;

namespace Shared.Events;

public class TaskStatusChangedEvent
{
    public Guid TaskId { get; set; }
    public TaskStatus NewStatus { get; set; }
}