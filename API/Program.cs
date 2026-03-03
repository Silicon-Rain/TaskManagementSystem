using Microsoft.EntityFrameworkCore;
using Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers()
	.AddJsonOptions(opts =>
	{
		opts.JsonSerializerOptions.Converters.Add(
			new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase));
	});

builder.Services.AddDbContext<PrimaryDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("PrimaryDb")));

builder.Services.AddHostedService<TaskStatusUpdateWorker>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
	try
	{
		var db = scope.ServiceProvider.GetRequiredService<PrimaryDbContext>();
    	await db.Database.MigrateAsync();
		Console.WriteLine("Database:TaskBusinessDb migrated successfully.");
	}
    catch (Exception ex)
	{
		Console.WriteLine($"Migration failed: {ex.Message}");
	}
}

app.MapControllers();

app.Run();
