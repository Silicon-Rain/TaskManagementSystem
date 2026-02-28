using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Worker.Data;

public class ConsumerOutboxPublisher : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IProducer<string, string> _producer;

    public ConsumerOutboxPublisher(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        var config = new ProducerConfig { BootstrapServers = "localhost:9092" };
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
                .ToListAsync(stoppingToken);

            foreach (var msg in messages)
            {
                await _producer.ProduceAsync("task-events", new Message<string, string> 
                { 
                    Key = msg.AggregateId.ToString(), 
                    Value = msg.Data 
                });
                msg.ProcessedOn = DateTime.UtcNow;
            }

            await db.SaveChangesAsync(stoppingToken);
            await Task.Delay(5000, stoppingToken);
        }
    }
}