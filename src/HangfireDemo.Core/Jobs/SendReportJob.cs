using Hangfire;
using HangfireDemo.Core.Jobs.Configuration;
using Microsoft.Extensions.Logging;

namespace HangfireDemo.Core.Jobs;

[AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 60, 120])]
public sealed class SendReportJob(ILogger<SendReportJob> logger)
{
    [Queue(HangfireQueues.Default)]
    public async Task ExecuteAsync(
        string batchId, string requestedBy, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Simulated report started. BatchId: {BatchId}, RequestedBy: {RequestedBy}",
            batchId, requestedBy);

        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

        logger.LogInformation(
            "Simulated report completed. BatchId: {BatchId}. No email was sent.", batchId);
    }
}
