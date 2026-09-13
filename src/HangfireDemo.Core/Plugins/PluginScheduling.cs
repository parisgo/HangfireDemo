using System.Text.Json;
using Cronos;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Hangfire.Storage;
using HangfireDemo.Core.Jobs.Configuration;

namespace HangfireDemo.Core.Plugins;

public sealed class PluginNotFoundException(string message) : Exception(message);
public sealed record PluginExecutionRequest(string Mode, JsonElement Parameters, int? DelayMinutes = null, Guid? BatchId = null, [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(200)] string? Name = null);
public sealed record PluginScheduleRequest(string PluginId, string JobId, JsonElement Parameters, string Cron, string TimeZone = "Europe/Paris", [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(200)] string? Name = null);
public sealed record PluginScheduleView(string ScheduleId, string PluginId, string JobId, JsonElement Parameters,
    string Cron, string TimeZone, DateTime? NextExecution, string? LastJobId, string? Error, string? Name = null);

public sealed class PluginScheduling(IPluginStore store, IBackgroundJobClient client, IRecurringJobManager recurring, JobStorage storage)
{
    private const string Prefix = "plugin:";

    public async Task RequireJobAsync(string pluginId, string jobId, CancellationToken ct)
    {
        PluginValidation.Identifier(pluginId);
        PluginValidation.Identifier(jobId);
        var active = await store.ActiveAsync(pluginId, ct);
        if (active is null || !active.Manifest.Jobs.Any(x => x.Id == jobId))
            throw new PluginNotFoundException("No active plugin task matches this ID.");
    }

    public static IState ExecutionState(PluginExecutionRequest request)
    {
        PluginValidation.Parameters(request.Parameters);
        return ExecutionState(request.Mode, request.DelayMinutes);
    }

    public static IState ExecutionState(string mode, int? delayMinutes)
    {
        if (mode == "Fire-and-forget" && delayMinutes is null) return new EnqueuedState("default");
        if (mode == "Delayed" && delayMinutes is >= 1 and <= 1440)
            return new ScheduledState(TimeSpan.FromMinutes(delayMinutes.Value));
        throw new PluginValidationException("Choose Fire-and-forget without delay, or Delayed with 1–1440 minutes.");
    }

    public static TimeZoneInfo ValidateSchedule(PluginScheduleRequest request)
    {
        PluginValidation.Parameters(request.Parameters);
        return ValidateSchedule(request.Cron, request.TimeZone);
    }

    public static TimeZoneInfo ValidateSchedule(string cron, string timeZone)
    {
        if (string.IsNullOrWhiteSpace(cron) || cron.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length != 5)
            throw new PluginValidationException("Use a five-field Cron expression.");
        try
        {
            CronExpression.Parse(cron, CronFormat.Standard);
            if (string.IsNullOrWhiteSpace(timeZone)) throw new PluginValidationException("Time zone is required.");
            return TimeZoneResolver.Resolve(timeZone);
        }
        catch (Exception ex) when (ex is CronFormatException or TimeZoneNotFoundException or InvalidTimeZoneException)
        { throw new PluginValidationException(ex.Message); }
    }

    private static Job Invocation(string pluginId, string jobId, JsonElement parameters, string requestedBy, string? batchId, string? name) =>
        Job.FromExpression<PluginJobRunner>(runner => runner.ExecuteAsync(pluginId, jobId, parameters.GetRawText(),
            requestedBy, batchId, null, CancellationToken.None, name));

    public async Task<(string JobId, string BatchId)> SubmitAsync(string pluginId, string jobId, PluginExecutionRequest request, string requestedBy, CancellationToken ct)
    {
        var state = ExecutionState(request);
        await RequireJobAsync(pluginId, jobId, ct);
        var batchId = (request.BatchId ?? Guid.NewGuid()).ToString("N");
        return (client.Create(Invocation(pluginId, jobId, request.Parameters, requestedBy, batchId, request.Name?.Trim()), state), batchId);
    }

    public async Task SaveAsync(string scheduleId, PluginScheduleRequest request, string requestedBy, CancellationToken ct)
    {
        PluginValidation.Identifier(scheduleId);
        var timeZone = ValidateSchedule(request);
        await RequireJobAsync(request.PluginId, request.JobId, ct);
        recurring.AddOrUpdate(Prefix + scheduleId, Invocation(request.PluginId, request.JobId, request.Parameters, requestedBy, null, request.Name?.Trim()),
            request.Cron, new RecurringJobOptions { TimeZone = timeZone });
    }

    public IReadOnlyList<PluginScheduleView> List()
    {
        using var connection = storage.GetConnection();
        return connection.GetRecurringJobs().Where(x => x.Id.StartsWith(Prefix, StringComparison.Ordinal)).Select(x =>
        {
            var args = x.Job?.Args;
            return new PluginScheduleView(x.Id[Prefix.Length..], args?[0]?.ToString() ?? "", args?[1]?.ToString() ?? "",
                JsonSerializer.Deserialize<JsonElement>(args?[2]?.ToString() ?? "{}"), x.Cron, x.TimeZoneId,
                x.NextExecution, x.LastJobId, x.Error, x.Job is null ? null : PluginExecutions.JobName(x.Job));
        }).ToArray();
    }

    public PluginScheduleView Get(string scheduleId)
    {
        PluginValidation.Identifier(scheduleId);
        return List().SingleOrDefault(x => x.ScheduleId == scheduleId)
            ?? throw new PluginNotFoundException("Schedule not found.");
    }

    public void Delete(string scheduleId) { Get(scheduleId); recurring.RemoveIfExists(Prefix + scheduleId); }
    public void Trigger(string scheduleId) { Get(scheduleId); recurring.Trigger(Prefix + scheduleId); }
}
