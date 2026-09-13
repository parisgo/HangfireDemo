using Hangfire;
using HangfireDemo.Core.Jobs.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace HangfireDemo.Core.Jobs.Health;

public sealed class HangfireServerHealthCheck(
    JobStorage storage,
    IOptions<HangfireSettings> settings) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var timeout = TimeSpan.FromSeconds(
                settings.Value.ServerHeartbeatTimeoutSeconds);
            var cutoff = DateTime.UtcNow.Subtract(timeout);
            var servers = storage.GetMonitoringApi().Servers();
            var activeServers = servers.Count(server => server.Heartbeat >= cutoff);

            if (activeServers == 0)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    $"Hangfire storage is reachable, but no server heartbeat was received in the last {timeout.TotalSeconds:N0} seconds.",
                    data: new Dictionary<string, object>
                    {
                        ["registeredServers"] = servers.Count,
                        ["activeServers"] = activeServers
                    }));
            }

            return Task.FromResult(HealthCheckResult.Healthy(
                "Hangfire storage is reachable and at least one Worker is active.",
                new Dictionary<string, object>
                {
                    ["registeredServers"] = servers.Count,
                    ["activeServers"] = activeServers
                }));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Hangfire storage is unavailable.",
                exception));
        }
    }
}
