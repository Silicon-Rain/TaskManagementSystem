using System.Text.Json;
using System.Text.Json.Serialization;
using Confluent.Kafka;
using Shared.Events;

public class TaskStatusUpdateWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TaskStatusUpdateWorker> _logger;

    public TaskStatusUpdateWorker(IServiceProvider serviceProvider, ILogger<TaskStatusUpdateWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = "localhost:9092",
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