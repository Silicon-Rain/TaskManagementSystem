using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Persistence;

public class ConsumerOutboxPublisher : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<ConsumerOutboxPublisher> _logger;

    public ConsumerOutboxPublisher(IServiceProvider serviceProvider, IConfiguration configuration, ILogger<ConsumerOutboxPublisher> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        var config = new ProducerConfig { BootstrapServers = configuration["Kafka:BootstrapServers"] };
        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SecondaryDbContext>();

            var messages = await db.OutboxMessages
                .Where(m => m.ProcessedOn == null)
                .OrderBy(m => m.OccuredOn)
                .Take(20)
                .ToListAsync(stoppingToken);

            foreach (var msg in messages)
            {
                try
                {
                    await _producer.ProduceAsync("task-events", new Message<string, string> 
                    { 
                        Key = msg.AggregateId.ToString(), 
                        Value = msg.Data,
                        Headers = new Headers
                        {
                            {"EventType", System.Text.Encoding.UTF8.GetBytes(msg.Type.ToString())}
                        }  
                    });
                    msg.ProcessedOn = DateTime.UtcNow;
                    await db.SaveChangesAsync(stoppingToken);
                }
                catch(Exception ex)
                {
                    _logger.LogError(ex, "Failed to publish message {MessageId}. Will retry later.", msg.Id);
                    break;
                }
                
            }

            await Task.Delay(5000, stoppingToken);
        }
    }
}