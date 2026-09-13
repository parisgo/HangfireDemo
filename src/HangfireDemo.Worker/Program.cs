using Hangfire;
using HangfireDemo.Core.Plugins;
using HangfireDemo.Core.Jobs.Configuration;
using HangfireDemo.Core.Jobs.Health;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

Console.OutputEncoding = new System.Text.UTF8Encoding(false);
var builder = WebApplication.CreateBuilder(args);
HangfireDemo.Core.Logging.ApplicationLogging.AddApplicationLogging(builder.Logging,
    builder.Configuration, builder.Environment.ContentRootPath, "Worker");

var hangfireSettings = builder.Configuration
    .GetSection(HangfireSettings.SectionName)
    .Get<HangfireSettings>() ?? new HangfireSettings();

builder.Services.AddHangfirePersistence(builder.Configuration);

builder.Services.AddPluginStorage(builder.Configuration);
builder.Services.AddPluginExecution();
HangfireDemo.Core.WebApi.WebApiServices.AddWebApiExecution(builder.Services);

builder.Services.AddHangfireServer(options =>
{
    options.ServerName = $"{Environment.MachineName}:{Environment.ProcessId}:worker";
    options.Queues = hangfireSettings.Queues;
    options.WorkerCount = hangfireSettings.WorkerCount > 0
        ? hangfireSettings.WorkerCount
        : Math.Max(Environment.ProcessorCount, 2);
});

builder.Services.AddHealthChecks()
    .AddCheck<HangfireServerHealthCheck>("hangfire", tags: ["ready"]);

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    service = "HangfireDemo.Worker",
    queues = hangfireSettings.Queues
}));

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = HealthResponseWriter.WriteAsync
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = HealthResponseWriter.WriteAsync
});
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = HealthResponseWriter.WriteAsync
});

app.Run();

public partial class Program;
