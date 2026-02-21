using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public class OutboxPublisherWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OutboxPublisherWorker> _logger;
    private readonly IProducer<string, string> _kafkaProducer;  
    private const string TopicName = "task-events";

    public OutboxPublisherWorker(IServiceProvider serviceProvider, ILogger<OutboxPublisherWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;

        var config = new ProducerConfig { BootstrapServers = "localhost:9092" };
        _kafkaProducer = new ProducerBuilder<string, string>(config).Build();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try 
            {
                await PublishOutboxMessages(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while publishing outbox messages.");
            }

            await Task.Delay(5000, stoppingToken);
        }
    }

    private async Task PublishOutboxMessages(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PrimaryDbContext>();

        var messages = await db.OutboxMessages
            .Where(m => m.ProcessedOn == null)
            .OrderBy(m => m.OccuredOn)
            .Take(20)
            .ToListAsync(stoppingToken);

        foreach (var message in messages)
        {
            _logger.LogInformation("Publishing message {Id} of type {Type}", message.Id, message.Type);

            var kafkaMessage = new Message<string, string> { 
                Key = message.Id.ToString(), 
                Value = message.Data 
            };

            await _kafkaProducer.ProduceAsync(TopicName, kafkaMessage, stoppingToken);

            message.ProcessedOn = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(stoppingToken);
    }
}