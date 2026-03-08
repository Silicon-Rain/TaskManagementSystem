namespace Worker;

public interface ITaskService
{
    Task<bool> ProcessTaskCreatedAsync(Guid taskId, CancellationToken ct);
    Task HandlePermanentFailureAsync(Guid taskId);
}