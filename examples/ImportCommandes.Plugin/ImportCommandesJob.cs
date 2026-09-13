using System.Text.Json;
using HangfireDemo.Core.Plugins;
using ImportCommandes.Plugin.Commandes;

namespace ImportCommandes.Plugin;

public sealed class ImportCommandesJob : IPluginJob
{
    public async Task ExecuteAsync(JobExecutionContext context, JsonElement parameters, CancellationToken cancellationToken)
    {
        var connectionName = parameters.TryGetProperty("connectionName", out var value)
            ? value.GetString() : "ApplicationConnection";
        if (string.IsNullOrWhiteSpace(connectionName)) throw new ArgumentException("connectionName is required.");
        var service = new SqlCommandeImportService(() => context.CreateConnection(connectionName), context.Logger);
        await service.ImportAsync(new(context.BatchId, context.RequestedBy, context.JobId), cancellationToken);
    }
}
