using System.Text.Json;
using System.Text.Json.Serialization;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Enums;
using Shared.Events;
using Shared.Messages;
using Worker.Data;

public class TaskProcessorWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TaskProcessorWorker> _logger;
    private readonly IConsumer<string, string> _consumer;

    public TaskProcessorWorker(IServiceProvider serviceProvider, ILogger<TaskProcessorWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;

        var config = new ConsumerConfig
        {
            BootstrapServers = "localhost:9092",
            GroupId = "task-processor-group",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };
        _consumer = new ConsumerBuilder<string, string>(config).Build();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _consumer.Subscribe("task-events");
        var serializerOptions = new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter() }
        };

        while (!stoppingToken.IsCancellationRequested)
        {
            var consumeResult = _consumer.Consume(stoppingToken);
            if (consumeResult == null) continue;

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SecondaryDbContext>();
            
            var taskId = Guid.Parse(consumeResult.Message.Key);
            
            if (await db.InboxMessages.AnyAsync(x => x.AggregateId == taskId && x.Type == EventType.TaskCreated))
            {
                _logger.LogWarning("Task {TaskId} already processed. Skipping.", taskId);
                _consumer.Commit(consumeResult);
                continue;
            }

            await using var transaction = await db.Database.BeginTransactionAsync(stoppingToken);
            try
            {
                db.OutboxMessages.Add(new OutboxMessage {
                    AggregateId = taskId,
                    Type = EventType.TaskStatusChanged,
                    Data = JsonSerializer.Serialize(new TaskStatusChangedEvent { TaskId = taskId, NewStatus = Shared.Enums.TaskStatus.Processing }, serializerOptions)
                });
                await db.SaveChangesAsync(stoppingToken);

                //SIMULATE BUSINESS LOGIC (Email/Logs)
                _logger.LogInformation("Processing Task {TaskId}...", taskId);
                await Task.Delay(1000); // Simulate work

                db.InboxMessages.Add(new InboxMessage 
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
                    Data = JsonSerializer.Serialize(new TaskStatusChangedEvent { TaskId = taskId, NewStatus = Shared.Enums.TaskStatus.Completed }, serializerOptions),
                };
                db.OutboxMessages.Add(statusUpdate);

                await db.SaveChangesAsync(stoppingToken);
                await transaction.CommitAsync(stoppingToken);

                _consumer.Commit(consumeResult);
                _logger.LogInformation("Task {TaskId} processed and status update queued.", taskId);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(stoppingToken);
                _logger.LogError(ex, "Error processing Task {TaskId}", taskId);

                await ReportFailure(taskId, serializerOptions);
                _consumer.Commit(consumeResult);
            }
        }
    }

    private async Task ReportFailure(Guid taskId, JsonSerializerOptions options) 
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SecondaryDbContext>();
        db.OutboxMessages.Add(new OutboxMessage {
            AggregateId = taskId,
            Type = EventType.TaskStatusChanged,
            Data = JsonSerializer.Serialize(new TaskStatusChangedEvent { TaskId = taskId, NewStatus = Shared.Enums.TaskStatus.Failed }, options)
        });
        await db.SaveChangesAsync();
    }
}