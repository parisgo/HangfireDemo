using HangfireDemo.Api.Security;
using HangfireDemo.Core.WebApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HangfireDemo.Api.Controllers;

[ApiController]
[ServiceFilter(typeof(PluginRequestFilter))]
[Authorize(Policy = SecurityPolicies.HangfireOperator)]
public sealed class WebApiJobsController(WebApiScheduling scheduling) : ControllerBase
{
    [HttpPost("api/webapi-jobs/executions")]
    public IActionResult Submit(WebApiExecutionRequest request)
    {
        var id = scheduling.Submit(request, User.Identity?.Name ?? "api-user");
        return Accepted(new { jobId = id, dashboardUrl = "/hangfire/jobs/details/" + id });
    }
    [HttpPut("api/webapi-schedules/{scheduleId}")]
    public IActionResult Save(string scheduleId, WebApiScheduleRequest request)
    {
        scheduling.Save(scheduleId, request, User.Identity?.Name ?? "api-user");
        return Ok(new { scheduleId });
    }
    [HttpDelete("api/webapi-schedules/{scheduleId}")]
    public IActionResult Delete(string scheduleId) { scheduling.Delete(scheduleId); return NoContent(); }
    [HttpPost("api/webapi-schedules/{scheduleId}/trigger")]
    public IActionResult Trigger(string scheduleId) { scheduling.Trigger(scheduleId); return Accepted(new { scheduleId }); }
}
