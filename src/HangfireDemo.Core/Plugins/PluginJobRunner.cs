using System.Text.Json;
using Hangfire;
using Hangfire.Server;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;

namespace HangfireDemo.Core.Plugins;

[AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 60, 120])]
public sealed class PluginJobRunner(PluginRuntime runtime, ILogger<PluginJobRunner> logger, IConfiguration? configuration = null)
{
    // Keep the original signature so existing persisted jobs remain executable.
    [Queue("default")]
    public async Task ExecuteAsync(string pluginId, string jobId, string parametersJson, string requestedBy,
        string? batchId, PerformContext? context, CancellationToken cancellationToken, string? name)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object?> { ["JobName"] = name });
        await ExecuteAsync(pluginId, jobId, parametersJson, requestedBy, batchId, context, cancellationToken);
    }

    [Queue("default")]
    public async Task ExecuteAsync(string pluginId, string jobId, string parametersJson, string requestedBy,
        string? batchId, PerformContext? context, CancellationToken cancellationToken)
    {
        var executionId = context?.BackgroundJob.Id ?? Guid.NewGuid().ToString("N");
        var execution = new JobExecutionContext(executionId, batchId ?? "hangfire:" + executionId, requestedBy, logger)
        {
            CreateConnection = name => new SqlConnection(configuration?.GetConnectionString(name)
                ?? throw new InvalidOperationException("Connection string is not configured: " + name))
        };
        using var parameters = JsonDocument.Parse(parametersJson);
        PluginValidation.Parameters(parameters.RootElement);
        var (job, version) = await runtime.CreateAsync(pluginId, jobId, cancellationToken);
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["PluginId"] = pluginId, ["PluginVersion"] = version, ["TaskId"] = jobId,
            ["HangfireJobId"] = executionId, ["BatchId"] = execution.BatchId
        });
        logger.LogInformation("Plugin task started using version {PluginVersion}", version);
        try
        {
            await job.ExecuteAsync(execution, parameters.RootElement, cancellationToken);
            logger.LogInformation("Plugin task completed using version {PluginVersion}", version);
        }
        finally
        {
            if (job is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync();
            else if (job is IDisposable disposable) disposable.Dispose();
        }
    }
}
