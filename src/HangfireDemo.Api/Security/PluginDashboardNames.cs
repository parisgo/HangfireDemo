using Hangfire.Common;
using HangfireDemo.Core.Plugins;

namespace HangfireDemo.Api.Security;

// One manifest lookup per Dashboard request, without loading plugin assemblies.
public sealed class PluginDashboardNames(IPluginStore store)
{
    private Dictionary<string, string> names = [];

    public async Task LoadAsync(CancellationToken ct)
    {
        names = (await store.ListAsync(ct))
            .Where(x => x.Status is "Active" or "Superseded").OrderBy(x => x.Sequence)
            .SelectMany(x => x.Manifest.Jobs.Select(job => (Key: x.Manifest.Id + "/" + job.Id, job.Name)))
            .GroupBy(x => x.Key).ToDictionary(x => x.Key, x => x.Last().Name);
    }

    public string? Resolve(Job job)
    {
        if (PluginExecutions.JobName(job) is { } name) return name;
        if (job.Type == typeof(HangfireDemo.Core.WebApi.WebApiJobRunner)) return $"WebAPI GET {job.Args.FirstOrDefault()}";
        if (job.Type != typeof(PluginJobRunner) || job.Args.Count < 2) return null;
        var key = $"{job.Args[0]}/{job.Args[1]}";
        return names.GetValueOrDefault(key, key);
    }
}
