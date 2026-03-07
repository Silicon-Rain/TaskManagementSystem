using System.Text.Json;
using System.Text.Json.Serialization;
using Confluent.Kafka;
using Shared.Events;
using Persistence;
using Shared.Enums;

public class TaskStatusUpdateWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TaskStatusUpdateWorker> _logger;
    private readonly IConfiguration _configuration;

    public TaskStatusUpdateWorker(IServiceProvider serviceProvider, ILogger<TaskStatusUpdateWorker> logger, IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = _configuration["Kafka:BootstrapServers"],
            GroupId = "api-status-updater-group",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe("task-events");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = consumer.Consume(stoppingToken);
                var taskId = Guid.Parse(result.Message.Key);

                var typeHeader = result.Message.Headers.GetLastBytes("EventType");
                var eventType = typeHeader != null ? System.Text.Encoding.UTF8.GetString(typeHeader) : null;
                if(eventType == null || eventType != EventType.TaskCreated.ToString())
                {
                    _logger.LogWarning("Received unsupported event type {EventType} for task {TaskId} while trying to update task status. Skipping.", eventType, taskId);
                    consumer.StoreOffset(result);
                    consumer.Commit(result);
                    return;
                }
            
                if (result != null && result.Message.Value.Contains("NewStatus")) 
                {
                    using var scope = _serviceProvider.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<PrimaryDbContext>();
                    
                    var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
                    var statusEvent = JsonSerializer.Deserialize<TaskStatusChangedEvent>(result.Message.Value, options);

                    if (statusEvent == null)
                    {
                        _logger.LogWarning("Failed to deserialize TaskStatusChangedEvent from message");
                        continue;
                    }

                    var task = await db.Tasks.FindAsync([statusEvent.TaskId], stoppingToken);
                    if (task != null)
                    {
                        // ONLY update if it's a "forward" progression
                        bool isValidUpdate = (task.Status, statusEvent.NewStatus) switch
                        {
                            (Shared.Enums.TaskStatus.Pending, _) => true, // Pending can move to anything
                            (Shared.Enums.TaskStatus.Processing, Shared.Enums.TaskStatus.Completed) => true,
                            (Shared.Enums.TaskStatus.Processing, Shared.Enums.TaskStatus.Failed) => true,
                            _ => false
                        };

                        if (isValidUpdate)
                        {
                            task.Status = statusEvent.NewStatus;
                            await db.SaveChangesAsync(stoppingToken);
                            _logger.LogInformation("Updated Task {Id} to {Status}", task.Id, task.Status);
                        }
                    }
                }
                consumer.Commit(result);
            }
            catch (ConsumeException ex) when (ex.Error.Reason.Contains("Unknown topic"))
            {
                _logger.LogWarning("Topic 'task-events' does not exist yet. Waiting for Producer to create it...");
                await Task.Delay(5000, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while consuming from Kafka. Retrying in 5s...");
                await Task.Delay(5000, stoppingToken);
            }            
        }
    }
}