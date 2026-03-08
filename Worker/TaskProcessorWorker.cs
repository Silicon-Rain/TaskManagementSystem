using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Enums;
using Microsoft.Extensions.Configuration;
using Polly.Retry;
using Polly;
using Npgsql;
using Worker;

public class TaskProcessorWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TaskProcessorWorker> _logger;
    private readonly IConfiguration _configuration;
    private readonly AsyncRetryPolicy _retryPolicy;
    private readonly SemaphoreSlim _semaphore;
    private const int MaxConcurrentTasks = 5;
    private readonly IProducer<string, string> _dlqProducer;
    private const string DlqTopic = "task-events-failed";
    private const string MainTopic = "task-events";

    public TaskProcessorWorker(IServiceScopeFactory scopeFactory, ILogger<TaskProcessorWorker> logger, IConfiguration configuration)
    {   
        _scopeFactory = scopeFactory;
        _logger = logger;
        _configuration = configuration;
        _semaphore = new SemaphoreSlim(MaxConcurrentTasks);

        var producerConfig = new ProducerConfig { BootstrapServers = _configuration["Kafka:BootstrapServers"] };
        _dlqProducer = new ProducerBuilder<string, string>(producerConfig).Build();

        _retryPolicy = Policy
            .Handle<DbUpdateException>()
            .Or<NpgsqlException>()
            .WaitAndRetryAsync(3,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                (exception, timeSpan, retryCount, context) => 
                {
                    _logger.LogWarning("Retry {Count} after {Delay}s due to: {Message}", 
                        retryCount, timeSpan.TotalSeconds, exception.Message);
                });
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
        consumer.Subscribe(MainTopic);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = consumer.Consume(stoppingToken);
                if (result == null) continue;

                await _semaphore.WaitAsync(stoppingToken);

                _ = ProcessMessageAsync(consumer, result, stoppingToken);                
            }
            catch (ConsumeException e) when (e.Error.Code == ErrorCode.UnknownTopicOrPart)
            {
                _logger.LogWarning("Topic 'task-events' not ready yet. Retrying in 5s...");
                await Task.Delay(5000, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Kafka encountered an error. Backing off...");
                await Task.Delay(5000, stoppingToken); 
            }
        }
    }

    private async Task ProcessMessageAsync(IConsumer<string, string> consumer, ConsumeResult<string, string> result, CancellationToken stoppingToken)
    {
        try
        {
            await _retryPolicy.ExecuteAsync(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                var taskService = scope.ServiceProvider.GetRequiredService<ITaskService>();

                _logger.LogInformation("Processing task {Offset} in parallel...", result.Offset);
                
                var taskId = Guid.Parse(result.Message.Key);

                var typeHeader = result.Message.Headers.GetLastBytes("EventType");
                var eventType = typeHeader != null ? System.Text.Encoding.UTF8.GetString(typeHeader) : null;
                if(eventType == null || eventType != EventType.TaskCreated.ToString())
                {
                    _logger.LogWarning("Received unsupported event type {EventType} for task {TaskId} while trying to process task. Skipping.", eventType, taskId);
                    consumer.StoreOffset(result);
                    consumer.Commit(result);
                    return;
                }
                
                await taskService.ProcessTaskCreatedAsync(taskId, stoppingToken);

                consumer.StoreOffset(result);
                consumer.Commit(result);
                _logger.LogInformation("Task {TaskId} processed and status update queued.", taskId);
            });            
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogCritical(ex, "Permanent failure for task {Offset}. Sending to DLQ.", result.Offset);

            try
            {
                var taskId = Guid.Parse(result.Message.Key);

                using var scope = _scopeFactory.CreateScope();
                var taskService = scope.ServiceProvider.GetRequiredService<ITaskService>();
                await taskService.HandlePermanentFailureAsync(taskId);
            }
            catch (Exception dbEx)
            {
                _logger.LogError(dbEx, "Database is DOWN. Proceeding to Kafka DLQ only.");
                return;
            }     

            await SendToDlq(result, ex.Message);       
            
            consumer.StoreOffset(result);
            consumer.Commit(result);    
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task SendToDlq(ConsumeResult<string, string> result, string errorMessage)
    {
        var dlqMessage = new Message<string, string>
        {
            Key = result.Message.Key,
            Value = result.Message.Value,
            Headers = result.Message.Headers ?? new Headers()
        };

        dlqMessage.Headers.Add("Error-Message", System.Text.Encoding.UTF8.GetBytes(errorMessage));
        dlqMessage.Headers.Add("Original-Offset", System.Text.Encoding.UTF8.GetBytes(result.Offset.ToString()));

        await _dlqProducer.ProduceAsync(DlqTopic, dlqMessage);
        _logger.LogWarning("Message {Key} moved to DLQ: {Topic}", result.Message.Key, DlqTopic);
    }
}