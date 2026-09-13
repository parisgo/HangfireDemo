using HangfireDemo.Api.Security;
using HangfireDemo.Core.Plugins;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HangfireDemo.Api.Controllers;

[ApiController]
[ServiceFilter(typeof(PluginRequestFilter))]
[Authorize(Policy = SecurityPolicies.HangfireReader)]
public sealed class PluginsController(IPluginStore store, PluginPackageInstaller installer, PluginScheduling scheduling, PluginExecutions executions) : ControllerBase
{
    [HttpGet("api/plugin-executions")]
    public async Task<IActionResult> Executions(CancellationToken ct) => Ok(await executions.ListAsync(ct));

    [HttpPost("api/plugin-executions/{id}/restart")]
    [Authorize(Policy = SecurityPolicies.HangfireOperator)]
    public IActionResult RestartExecution(string id, PluginExecutionAction action)
    {
        executions.Change(id, action, restart: true);
        return Accepted(new { id });
    }

    [HttpDelete("api/plugin-executions/{id}")]
    [Authorize(Policy = SecurityPolicies.HangfireOperator)]
    public IActionResult DeleteExecution(string id, PluginExecutionAction action)
    {
        executions.Change(id, action, restart: false);
        return NoContent();
    }

    [HttpGet("api/plugins")]
    public async Task<IActionResult> Plugins(CancellationToken ct) => Ok(new
    {
        versions = (await store.ListAsync(ct)).Where(x => x.Status != "Deleted")
            .Select(x => new { x.Sequence, x.Manifest.Id, x.Manifest.Version, x.Status, x.Error,
                jobs = x.Manifest.Jobs.Select(job => job.Name) }),
        workers = await store.WorkersAsync(ct)
    });

    [HttpDelete("api/plugins/{sequence:long}")]
    [Authorize(Policy = SecurityPolicies.HangfireAdmin)]
    public async Task<IActionResult> DeletePlugin(long sequence, CancellationToken ct)
    {
        await store.DeleteAsync(sequence, ct);
        return NoContent();
    }

    [HttpPost("api/plugins")]
    [Authorize(Policy = SecurityPolicies.HangfireAdmin)]
    [RequestSizeLimit(51 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 50 * 1024 * 1024)]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken ct)
    {
        if (file.Length == 0 || !file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            throw new PluginValidationException("Select a non-empty ZIP plugin package.");
        await using var stream = file.OpenReadStream();
        var manifest = await installer.InstallAsync(stream, ct);
        return Accepted(new { manifest.Id, manifest.Version, status = "Pending" });
    }

    [HttpGet("api/plugin-jobs")]
    public async Task<IActionResult> Jobs(CancellationToken ct) => Ok((await store.ListAsync(ct))
        .Where(x => x.Status == "Active").SelectMany(x => x.Manifest.Jobs.Select(job => new
        { pluginId = x.Manifest.Id, x.Manifest.Version, jobId = job.Id, job.Name, job.ParametersExample })));

    [HttpPost("api/plugin-jobs/{pluginId}/{jobId}/executions")]
    [Authorize(Policy = SecurityPolicies.HangfireOperator)]
    public async Task<IActionResult> Submit(string pluginId, string jobId, PluginExecutionRequest request, CancellationToken ct)
    {
        var result = await scheduling.SubmitAsync(pluginId, jobId, request, User.Identity?.Name ?? "api-user", ct);
        return Accepted(new { jobId = result.JobId, batchId = result.BatchId, dashboardUrl = "/hangfire/jobs/details/" + result.JobId });
    }

    [HttpGet("api/plugin-schedules")]
    public IActionResult Schedules() => Ok(scheduling.List());

    [HttpGet("api/plugin-schedules/{scheduleId}")]
    public IActionResult Schedule(string scheduleId) => Ok(scheduling.Get(scheduleId));

    [HttpPut("api/plugin-schedules/{scheduleId}")]
    [Authorize(Policy = SecurityPolicies.HangfireOperator)]
    public async Task<IActionResult> Save(string scheduleId, PluginScheduleRequest request, CancellationToken ct)
    {
        await scheduling.SaveAsync(scheduleId, request, User.Identity?.Name ?? "api-user", ct);
        return Ok(new { scheduleId });
    }

    [HttpDelete("api/plugin-schedules/{scheduleId}")]
    [Authorize(Policy = SecurityPolicies.HangfireOperator)]
    public IActionResult Delete(string scheduleId) { scheduling.Delete(scheduleId); return NoContent(); }

    [HttpPost("api/plugin-schedules/{scheduleId}/trigger")]
    [Authorize(Policy = SecurityPolicies.HangfireOperator)]
    public IActionResult Trigger(string scheduleId) { scheduling.Trigger(scheduleId); return Accepted(new { scheduleId }); }
}
