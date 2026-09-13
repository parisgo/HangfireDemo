using Hangfire;
using Hangfire.States;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using HangfireDemo.Core.WebApi;

namespace HangfireDemo.Core.Plugins;

public sealed record PluginExecutionView(string Id, string Name, string PluginId, string JobId, string State, bool CanRestart, bool CanDelete, string TaskType, string JobKind = "Plugin", string? Url = null);
public sealed record PluginExecutionAction(string ExpectedState);

public sealed class PluginExecutions(IPluginStore store, JobStorage storage, IBackgroundJobClient client, IConfiguration configuration)
{
    public static bool CanRestart(string state) => state is "Succeeded" or "Failed" or "Deleted" or "Scheduled";

    public async Task<IReadOnlyList<PluginExecutionView>> ListAsync(CancellationToken ct)
    {
        // This platform uses Hangfire.SqlServer with the default HangFire schema.
        // Filter in SQL so older plugin executions are not hidden by unrelated jobs.
        await using var sql = new SqlConnection(configuration.GetConnectionString("HangfireConnection"));
        await sql.OpenAsync(ct);
        await using var command = new SqlCommand("""
            SELECT TOP (100) j.Id, firstState.Name,
                CAST(CASE WHEN EXISTS (SELECT 1 FROM [HangFire].[JobParameter] p
                    WHERE p.JobId = j.Id AND p.Name = N'RecurringJobId' AND p.Value <> N'null') THEN 1 ELSE 0 END AS bit)
            FROM [HangFire].[Job] j
            OUTER APPLY (SELECT TOP (1) s.Name FROM [HangFire].[State] s
                WHERE s.JobId = j.Id ORDER BY s.Id) firstState
            WHERE (COALESCE(JSON_VALUE(InvocationData, '$.t'), JSON_VALUE(InvocationData, '$.Type')) LIKE @type
                OR COALESCE(JSON_VALUE(InvocationData, '$.t'), JSON_VALUE(InvocationData, '$.Type')) LIKE @webApiType)
                AND (j.StateName IS NULL OR j.StateName <> N'Deleted')
            ORDER BY j.Id DESC
            """, sql);
        command.Parameters.AddWithValue("@type", typeof(PluginJobRunner).FullName + ",%");
        command.Parameters.AddWithValue("@webApiType", typeof(WebApiJobRunner).FullName + ",%");
        var ids = new List<(string Id, string TaskType)>();
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) ids.Add((reader.GetInt64(0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                ResolveTaskType(reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetBoolean(2))));
        var names = (await store.ListAsync(ct)).Where(x => x.Status is "Active" or "Superseded").OrderBy(x => x.Sequence)
            .SelectMany(x => x.Manifest.Jobs.Select(job => (Key: x.Manifest.Id + "/" + job.Id, job.Name)))
            .GroupBy(x => x.Key).ToDictionary(x => x.Key, x => x.Last().Name);
        using var connection = storage.GetConnection();
        var result = new List<PluginExecutionView>();
        foreach (var (id, taskType) in ids)
        {
            var data = connection.GetJobData(id);
            if (data?.Job is null || !Supported(data.Job.Type) || data.Job.Args.Count < 2) continue;
            if (data.Job.Type == typeof(WebApiJobRunner))
            {
                var webState = data.State ?? "Unknown";
                if (webState != "Deleted")
                    result.Add(new(id, JobName(data.Job) ?? "WebAPI GET", "", "", webState, CanRestart(webState), true, taskType,
                        "WebAPI", data.Job.Args[0]?.ToString()));
                continue;
            }
            var pluginId = data.Job.Args[0]?.ToString() ?? "";
            var jobId = data.Job.Args[1]?.ToString() ?? "";
            var key = pluginId + "/" + jobId;
            var state = data.State ?? "Unknown";
            if (state == "Deleted") continue;
            result.Add(new(id, JobName(data.Job) ?? names.GetValueOrDefault(key, key), pluginId, jobId, state, CanRestart(state), state != "Deleted", taskType));
        }
        return result;
    }

    // Use the original state, since retries also enter Scheduled and restarts enter Enqueued.
    public static string ResolveTaskType(string? initialState, bool recurring) => recurring ? "Recurring" : initialState switch
    {
        "Scheduled" => "Delayed",
        "Enqueued" => "Fire-and-forget",
        _ => "Unknown"
    };

    public void Change(string id, PluginExecutionAction action, bool restart)
    {
        if (!long.TryParse(id, out var numericId) || numericId < 1)
            throw new PluginValidationException("Invalid execution ID.");
        using var connection = storage.GetConnection();
        var data = connection.GetJobData(id);
        if (data?.Job is null || !Supported(data.Job.Type))
            throw new PluginNotFoundException("Plugin execution not found.");
        if (string.IsNullOrWhiteSpace(action.ExpectedState) || data.State != action.ExpectedState)
            throw new PluginConflictException("Task state changed. Refresh the list and try again.");
        if (restart && !CanRestart(data.State))
            throw new PluginConflictException("A queued or running task cannot be restarted.");
        if (!restart && data.State == "Deleted") return;
        IState next = restart ? new EnqueuedState("default") : new DeletedState();
        if (!client.ChangeState(id, next, action.ExpectedState))
            throw new PluginConflictException("Task state changed. Refresh the list and try again.");
    }
    public static string? JobName(Hangfire.Common.Job job)
    {
        if (!Supported(job.Type)) return null;
        var index = Array.FindIndex(job.Method.GetParameters(), p => p.Name == "name");
        var name = index >= 0 && index < job.Args.Count ? job.Args[index]?.ToString() : null;
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private static bool Supported(Type type) => type == typeof(PluginJobRunner) || type == typeof(WebApiJobRunner);
}
