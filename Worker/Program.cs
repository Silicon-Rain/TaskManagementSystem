using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Persistence;
using Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<SecondaryDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("SecondaryDb")));

builder.Services.AddHostedService<TaskProcessorWorker>();
builder.Services.AddHostedService<ConsumerOutboxPublisher>();
builder.Services.AddScoped<ITaskService, TaskService>();

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
	try
	{
		var db = scope.ServiceProvider.GetRequiredService<SecondaryDbContext>();
    	await db.Database.MigrateAsync();
		Console.WriteLine("Database:TaskWorkerDb migrated successfully.");
	}
    catch (Exception ex)
	{
		Console.WriteLine($"Migration failed: {ex.Message}");
	}
}

host.Run();