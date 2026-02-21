using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<PrimaryDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("PrimaryDb")));
    
builder.Services.AddHostedService<OutboxPublisherWorker>();

var host = builder.Build();
host.Run();