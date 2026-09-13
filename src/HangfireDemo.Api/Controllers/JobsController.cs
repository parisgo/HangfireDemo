using Hangfire;
using Hangfire.States;
using HangfireDemo.Api.Security;
using HangfireDemo.Core.Jobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HangfireDemo.Api.Controllers;

[ApiController]
[Authorize(Policy = SecurityPolicies.HangfireOperator)]
[Route("api/jobs")]
public sealed class JobsController(
    IBackgroundJobClient backgroundJobs,
    IRecurringJobManager recurringJobs,
    JobCatalog catalog) : ControllerBase
{
    [HttpGet]
    public IActionResult List() => Ok(catalog.Definitions.Select(job => new
    {
        job.Name,
        job.Queue,
        job.RecurringJobId
    }));

    [HttpPost("{jobName}")]
    public IActionResult Enqueue(string jobName, [FromQuery] Guid? batchId = null)
        => Submit(jobName, batchId, null);

    [HttpPost("{jobName}/delayed")]
    public IActionResult Schedule(
        string jobName, [FromQuery] int minutes = 10, [FromQuery] Guid? batchId = null)
    {
        if (minutes is < 1 or > 1440)
        {
            return BadRequest("minutes must be between 1 and 1440.");
        }

        return Submit(jobName, batchId, minutes);
    }

    [HttpPost("{jobName}/trigger-recurring")]
    public IActionResult TriggerRecurring(string jobName)
    {
        if (!catalog.TryGet(jobName, out var definition))
        {
            return NotFound(new { error = "Unknown job.", jobName });
        }

        if (definition.RecurringJobId is null)
        {
            return BadRequest(new { error = "This job has no recurring schedule.", jobName });
        }

        recurringJobs.Trigger(definition.RecurringJobId);
        return Accepted(new { recurringJobId = definition.RecurringJobId });
    }

    private IActionResult Submit(string jobName, Guid? batchId, int? minutes)
    {
        if (!catalog.TryGet(jobName, out var definition))
        {
            return NotFound(new { error = "Unknown job.", jobName });
        }

        var effectiveBatchId = (batchId ?? Guid.NewGuid()).ToString("N");
        var requestedBy = User.Identity?.Name ?? "api-user";
        var job = definition.CreateJob(effectiveBatchId, requestedBy);
        IState state = minutes.HasValue
            ? new ScheduledState(TimeSpan.FromMinutes(minutes.Value))
            : new EnqueuedState(definition.Queue);
        var jobId = backgroundJobs.Create(job, state);

        return Accepted(new
        {
            jobId,
            jobName = definition.Name,
            batchId = effectiveBatchId,
            executeAfterMinutes = minutes
        });
    }
}
