using Hangfire;
using Hangfire.Server;
using HangfireDemo.Core.Commandes;
using HangfireDemo.Core.Jobs.Configuration;
using Microsoft.Extensions.Logging;

namespace HangfireDemo.Core.Jobs;

[AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 60, 120])]
public sealed class ImportCommandeJob(
    ICommandeImportService importService,
    ILogger<ImportCommandeJob> logger)
{
    [Queue(HangfireQueues.Imports)]
    [DisableConcurrentExecution(timeoutInSeconds: 10 * 60)]
    public async Task ExecuteAsync(
        string? batchId,
        string? requestedBy,
        PerformContext? context,
        CancellationToken cancellationToken)
    {
        var jobId = context?.BackgroundJob.Id;
        var effectiveBatchId = batchId ?? $"hangfire:{jobId ?? Guid.NewGuid().ToString("N")}";
        var effectiveRequestedBy = requestedBy ?? "scheduler";

        using var logScope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["BatchId"] = effectiveBatchId,
            ["HangfireJobId"] = jobId,
            ["RequestedBy"] = effectiveRequestedBy
        });

        logger.LogInformation("Commande import started");

        var result = await importService.ImportAsync(
            new CommandeImportRequest(
                effectiveBatchId,
                effectiveRequestedBy,
                jobId),
            cancellationToken);

        logger.LogInformation(
            "Commande import finished with status {ImportStatus}",
            result.Status);
    }
}
