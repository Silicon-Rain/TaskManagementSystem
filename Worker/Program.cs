using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Worker.Data;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<SecondaryDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("SecondaryDb")));

builder.Services.AddHostedService<TaskProcessorWorker>();
builder.Services.AddHostedService<ConsumerOutboxPublisher>();

var host = builder.Build();
host.Run();