using Hangfire;
using Hangfire.Common;
using Hangfire.Storage;
using HangfireDemo.Core.Plugins;

namespace HangfireDemo.Core.WebApi;

public sealed record WebApiExecutionRequest(string Url, string Mode, int? DelayMinutes = null, [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(200)] string? Name = null);
public sealed record WebApiScheduleRequest(string Url, string Cron, string TimeZone = "Europe/Paris", [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(200)] string? Name = null);
public sealed record WebApiScheduleView(string ScheduleId, string Url, string Cron, string TimeZone, DateTime? NextExecution, string? Name = null);

public sealed class WebApiScheduling(IBackgroundJobClient client, IRecurringJobManager recurring, JobStorage storage)
{
    private static Job Invocation(string url, string requestedBy, string? name) => Job.FromExpression<WebApiJobRunner>(
        runner => runner.ExecuteAsync(url, requestedBy, null, CancellationToken.None, name));

    public string Submit(WebApiExecutionRequest request, string requestedBy)
    {
        var url = WebApiValidation.Url(request.Url);
        var state = PluginScheduling.ExecutionState(request.Mode, request.DelayMinutes);
        return client.Create(Invocation(url, requestedBy, request.Name?.Trim()), state);
    }

    public void Save(string scheduleId, WebApiScheduleRequest request, string requestedBy)
    {
        PluginValidation.Identifier(scheduleId);
        var url = WebApiValidation.Url(request.Url);
        var zone = PluginScheduling.ValidateSchedule(request.Cron, request.TimeZone);
        recurring.AddOrUpdate("webapi:" + scheduleId, Invocation(url, requestedBy, request.Name?.Trim()), request.Cron,
            new RecurringJobOptions { TimeZone = zone });
    }

    public WebApiScheduleView Get(string scheduleId)
    {
        PluginValidation.Identifier(scheduleId);
        using var connection = storage.GetConnection();
        var item = connection.GetRecurringJobs().SingleOrDefault(x => x.Id == "webapi:" + scheduleId)
            ?? throw new PluginNotFoundException("Schedule not found.");
        return new(scheduleId, item.Job.Args[0]?.ToString() ?? "", item.Cron, item.TimeZoneId, item.NextExecution, PluginExecutions.JobName(item.Job));
    }
    public void Delete(string scheduleId) { Get(scheduleId); recurring.RemoveIfExists("webapi:" + scheduleId); }
    public void Trigger(string scheduleId) { Get(scheduleId); recurring.Trigger("webapi:" + scheduleId); }
}
