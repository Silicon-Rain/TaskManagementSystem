using Shared.Events;

namespace API;

public interface ITaskUpdateService
{
    Task UpdateStatusAsync(Guid taskId, TaskStatusChangedEvent statusEvent, CancellationToken ct);
}