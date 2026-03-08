using System.Text.Json;
using System.Text.Json.Serialization;
using Confluent.Kafka;
using Shared.Events;
using Persistence;
using Shared.Enums;
using Polly.Retry;
using Polly;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using API;
using System.Text;

public class TaskStatusUpdateWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TaskStatusUpdateWorker> _logger;
    private readonly IConfiguration _configuration;
    private readonly AsyncRetryPolicy _retryPolicy;
    private readonly SemaphoreSlim _semaphore;
    private readonly IProducer<string, string> _dlqProducer;
    private const int MaxConcurrentUpdates = 5;
    private const string DlqTopic = "task-status-updates-failed";

    public TaskStatusUpdateWorker(IServiceScopeFactory scopeFactory, ILogger<TaskStatusUpdateWorker> logger, IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _configuration = configuration;
        _semaphore = new SemaphoreSlim(MaxConcurrentUpdates);

        _retryPolicy = Policy
            .Handle<DbUpdateException>()
            .Or<NpgsqlException>()
            .WaitAndRetryAsync(3, i => TimeSpan.FromSeconds(Math.Pow(2, i)));

        var pConfig = new ProducerConfig { BootstrapServers = _configuration["Kafka:BootstrapServers"] };
        _dlqProducer = new ProducerBuilder<string, string>(pConfig).Build();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = _configuration["Kafka:BootstrapServers"],
            GroupId = "api-status-updater-group",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe("task-events");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = consumer.Consume(stoppingToken);
                if(result == null || result.Message == null)
                    continue;

                var taskId = Guid.Parse(result.Message.Key);

                var typeHeader = result.Message.Headers.GetLastBytes("EventType");
                var eventType = typeHeader != null ? Encoding.UTF8.GetString(typeHeader) : null;
                if(eventType == null || eventType != EventType.TaskStatusChanged.ToString())
                {
                    _logger.LogWarning("Received unsupported event type {EventType} for task {TaskId} while trying to update task status. Skipping.", eventType, taskId);
                    consumer.StoreOffset(result);
                    consumer.Commit(result);
                    continue;
                }
                
                var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
                var statusEvent = JsonSerializer.Deserialize<TaskStatusChangedEvent>(result.Message.Value, options);

                if (statusEvent == null)
                {
                    _logger.LogWarning("Failed to deserialize TaskStatusChangedEvent from message");
                    continue;
                }

                await _semaphore.WaitAsync(stoppingToken);
                _ = ProcessUpdateAsync(consumer, result, statusEvent, stoppingToken);
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
    
    private async Task ProcessUpdateAsync(IConsumer<string, string> consumer, ConsumeResult<string, string> result, TaskStatusChangedEvent? taskStatusChangedEvent, CancellationToken ct)
    {
        var taskId = Guid.Parse(result.Message.Key);
        try
        {
            await _retryPolicy.ExecuteAsync(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                var updateService = scope.ServiceProvider.GetRequiredService<ITaskUpdateService>();

                if (taskStatusChangedEvent != null)
                {
                    await updateService.UpdateStatusAsync(taskId, taskStatusChangedEvent, ct);
                }

                consumer.StoreOffset(result);
                consumer.Commit(result);
            });
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Failed to update status for {TaskId}. Ejecting to DLQ.", taskId);
            await SendToDlq(result, ex.Message);
            
            consumer.StoreOffset(result);
            consumer.Commit(result);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task SendToDlq(ConsumeResult<string, string> result, string error)
    {
        var msg = new Message<string, string> { Key = result.Message.Key, Value = result.Message.Value, Headers = result.Message.Headers };
        msg.Headers.Add("Error", Encoding.UTF8.GetBytes(error));
        await _dlqProducer.ProduceAsync(DlqTopic, msg);
    }
}