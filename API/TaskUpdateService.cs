using Persistence;
using Shared.Events;

namespace API;

public class TaskUpdateService : ITaskUpdateService
{
    private readonly PrimaryDbContext _db;
    private readonly ILogger<TaskUpdateService> _logger;

    public TaskUpdateService(PrimaryDbContext db, ILogger<TaskUpdateService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task UpdateStatusAsync(Guid taskId, TaskStatusChangedEvent statusEvent, CancellationToken ct)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var task = await _db.Tasks.FindAsync([taskId], ct);
            if (task == null)
            {
                _logger.LogWarning("Task {Id} not found in Primary DB. Skipping update.", taskId);
                return;
            }
            
            bool isValidUpdate = (task.Status, statusEvent.NewStatus) switch
            {
                (Shared.Enums.TaskStatus.Pending, _) => true,
                (Shared.Enums.TaskStatus.Processing, Shared.Enums.TaskStatus.Completed) => true,
                (Shared.Enums.TaskStatus.Processing, Shared.Enums.TaskStatus.Failed) => true,
                _ => false
            };

            if (isValidUpdate)
            {
                task.Status = statusEvent.NewStatus;
                await _db.SaveChangesAsync(ct);
                _logger.LogInformation("Updated Task {Id} to {Status} in Primary DB", taskId, task.Status);
            }
        }
        catch (Exception)
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }
}