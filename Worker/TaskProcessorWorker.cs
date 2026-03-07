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
using Persistence;
using Microsoft.Extensions.Configuration;

public class TaskProcessorWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TaskProcessorWorker> _logger;
    private readonly IConfiguration _configuration;
    private readonly SemaphoreSlim _semaphore;
    private const int MaxConcurrentTasks = 5;

    public TaskProcessorWorker(IServiceScopeFactory scopeFactory, ILogger<TaskProcessorWorker> logger, IConfiguration configuration)
    {   
        _scopeFactory = scopeFactory;
        _logger = logger;
        _configuration = configuration;
        _semaphore = new SemaphoreSlim(MaxConcurrentTasks);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = _configuration["Kafka:BootstrapServers"],
            GroupId = "task-processor-group",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe("task-events");

        var serializerOptions = new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter() }
        };

        while (!stoppingToken.IsCancellationRequested)
        {
            var result = consumer.Consume(stoppingToken);
            if (result == null) continue;

            await _semaphore.WaitAsync(stoppingToken);

            _ = ProcessMessageAsync(consumer, result, serializerOptions, stoppingToken);
        }
    }

    private async Task ProcessMessageAsync(IConsumer<string, string> consumer, ConsumeResult<string, string> result, JsonSerializerOptions serializerOptions, CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SecondaryDbContext>();

            _logger.LogInformation("Processing task {Offset} in parallel...", result.Offset);
            
            var taskId = Guid.Parse(result.Message.Key);
            
            if (await db.InboxMessages.AnyAsync(x => x.AggregateId == taskId && x.Type == EventType.TaskCreated))
            {
                _logger.LogWarning("Task {TaskId} already processed. Skipping.", taskId);
                consumer.StoreOffset(result);
                consumer.Commit(result);
                return;
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
                await Task.Delay(1000, stoppingToken); // Simulate work

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

                consumer.StoreOffset(result);
                _logger.LogInformation("Storing offset for task {TaskId}", taskId);
                consumer.Commit(result);
                _logger.LogInformation("Task {TaskId} processed and status update queued.", taskId);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(stoppingToken);
                _logger.LogError(ex, "Error processing Task {TaskId}", taskId);

                await ReportFailure(taskId, consumer, result, serializerOptions);
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task ReportFailure(Guid taskId, IConsumer<string, string> consumer, ConsumeResult<string, string> result, JsonSerializerOptions options) 
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SecondaryDbContext>();
        
        db.OutboxMessages.Add(new OutboxMessage {
            AggregateId = taskId,
            Type = EventType.TaskStatusChanged,
            Data = JsonSerializer.Serialize(new TaskStatusChangedEvent { TaskId = taskId, NewStatus = Shared.Enums.TaskStatus.Failed }, options)
        });
        db.InboxMessages.Add(new InboxMessage { AggregateId = taskId, Type = EventType.TaskCreated, ReceivedOn = DateTime.UtcNow,
                    ProcessedOn = DateTime.UtcNow });

        await db.SaveChangesAsync();
        consumer.StoreOffset(result);
        _logger.LogInformation("Storing offset for failed task {TaskId}", taskId);
    }
}