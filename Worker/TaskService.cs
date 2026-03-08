using Microsoft.Extensions.Logging;
using Persistence;
using Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Shared.Messages;
using System.Text.Json;
using Shared.Events;
using System.Text.Json.Serialization;

namespace Worker;

public class TaskService : ITaskService
{
    private readonly SecondaryDbContext _db;
    private readonly ILogger<TaskService> _logger;
    private readonly JsonSerializerOptions _serializerOptions;

    public TaskService(SecondaryDbContext db, ILogger<TaskService> logger)
    {
        _db = db;
        _logger = logger;

        _serializerOptions = new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter() }
        };
    }

    public async Task<bool> ProcessTaskCreatedAsync(Guid taskId, CancellationToken ct)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            if (await _db.InboxMessages.AnyAsync(x => x.AggregateId == taskId && x.Type == EventType.TaskCreated))
            {
                _logger.LogWarning("Task {TaskId} already processed. Signalling worker to skip.", taskId);
                return true;
            }
            
            _db.OutboxMessages.Add(new OutboxMessage {
                AggregateId = taskId,
                Type = EventType.TaskStatusChanged,
                Data = JsonSerializer.Serialize(new TaskStatusChangedEvent { TaskId = taskId, NewStatus = Shared.Enums.TaskStatus.Processing }, _serializerOptions)
            });
            await _db.SaveChangesAsync(ct);

            //SIMULATE BUSINESS LOGIC (Email/Logs)
            _logger.LogInformation("Processing Task {TaskId}...", taskId);
            await Task.Delay(1000, ct); // Simulate work

            _db.InboxMessages.Add(new InboxMessage 
            { 
                AggregateId = taskId, 
                Type = EventType.TaskCreated, 
                ReceivedOn = DateTime.UtcNow,
                ProcessedOn = DateTime.UtcNow 
            });                

            var statusUpdate = new OutboxMessage
            {
                AggregateId = taskId,
                Type = EventType.TaskStatusChanged,
                Data = JsonSerializer.Serialize(new TaskStatusChangedEvent { TaskId = taskId, NewStatus = Shared.Enums.TaskStatus.Completed }, _serializerOptions),
            };
            _db.OutboxMessages.Add(statusUpdate);

            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return true;
        }
        catch (Exception)
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public async Task HandlePermanentFailureAsync(Guid taskId)
    {
        _db.OutboxMessages.Add(new OutboxMessage {
            AggregateId = taskId,
            Type = EventType.TaskStatusChanged,
            Data = JsonSerializer.Serialize(new TaskStatusChangedEvent { TaskId = taskId, NewStatus = Shared.Enums.TaskStatus.Failed }, _serializerOptions)
        });
        _db.InboxMessages.Add(new InboxMessage { AggregateId = taskId, Type = EventType.TaskCreated, ReceivedOn = DateTime.UtcNow,
                    ProcessedOn = DateTime.UtcNow });

        await _db.SaveChangesAsync();
    }
}