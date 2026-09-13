using Hangfire;
using HangfireDemo.Api.Security;
using HangfireDemo.Core.Jobs;
using HangfireDemo.Core.Jobs.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HangfireDemo.Api.Controllers;

[ApiController]
[Authorize(Policy = SecurityPolicies.HangfireOperator)]
[Route("api/jobs")]
public sealed class JobsController(
    IBackgroundJobClient backgroundJobs,
    IRecurringJobManager recurringJobs) : ControllerBase
{
    [HttpPost("import-commandes")]
    public IActionResult EnqueueImport([FromQuery] Guid? batchId = null)
    {
        var effectiveBatchId = (batchId ?? Guid.NewGuid()).ToString("N");
        var requestedBy = User.Identity?.Name ?? "api-user";

        var jobId = backgroundJobs.Enqueue<ImportCommandeJob>(job =>
            job.ExecuteAsync(
                effectiveBatchId,
                requestedBy,
                null,
                CancellationToken.None));

        return Accepted(new { jobId, batchId = effectiveBatchId });
    }

    [HttpPost("import-commandes/delayed")]
    public IActionResult ScheduleImport(
        [FromQuery] int minutes = 10,
        [FromQuery] Guid? batchId = null)
    {
        if (minutes is < 1 or > 1440)
        {
            return BadRequest("minutes must be between 1 and 1440.");
        }

        var effectiveBatchId = (batchId ?? Guid.NewGuid()).ToString("N");
        var requestedBy = User.Identity?.Name ?? "api-user";
        var jobId = backgroundJobs.Schedule<ImportCommandeJob>(job =>
                job.ExecuteAsync(
                    effectiveBatchId,
                    requestedBy,
                    null,
                    CancellationToken.None),
            TimeSpan.FromMinutes(minutes));

        return Accepted(new
        {
            jobId,
            batchId = effectiveBatchId,
            executeAfterMinutes = minutes
        });
    }

    [HttpPost("import-commandes/trigger-recurring")]
    public IActionResult TriggerRecurringImport()
    {
        recurringJobs.Trigger(HangfireJobIds.DailyCommandeImport);
        return Accepted(new { recurringJobId = HangfireJobIds.DailyCommandeImport });
    }
}
