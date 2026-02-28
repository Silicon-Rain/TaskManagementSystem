using Microsoft.EntityFrameworkCore;

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

app.MapControllers();

app.Run();
