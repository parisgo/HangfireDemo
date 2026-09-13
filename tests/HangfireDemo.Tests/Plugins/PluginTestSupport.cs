using System.Text.Json;
using HangfireDemo.Core.Plugins;
using Microsoft.Extensions.Logging;

namespace HangfireDemo.Tests.Plugins;

internal sealed class MemoryPluginStore : IPluginStore
{
    public List<PluginVersion> Versions { get; } = [];
    public List<PluginWorkerStatus> Workers { get; } = [];
    public Task<IReadOnlyList<PluginVersion>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<PluginVersion>>(Versions.ToArray());
    public Task AddAsync(PluginManifest manifest, string directory, CancellationToken ct)
    {
        if (Versions.Any(x => x.Manifest.Id == manifest.Id && x.Manifest.Version == manifest.Version)) throw new PluginConflictException("Duplicate version");
        Versions.Add(new(Versions.Count + 1, manifest, directory, "Pending", null));
        return Task.CompletedTask;
    }
    public Task<PluginVersion?> ActiveAsync(string id, CancellationToken ct) => Task.FromResult(Versions.SingleOrDefault(x => x.Manifest.Id == id && x.Status == "Active"));
    public Task ActivateAsync(PluginVersion version, CancellationToken ct)
    {
        if (Versions.Any(x => x.Sequence == version.Sequence && x.Status == "Deleted")) return Task.CompletedTask;
        var current = Versions.FirstOrDefault(x => x.Manifest.Id == version.Manifest.Id && x.Status == "Active");
        var active = Math.Max(current?.Sequence ?? 0, version.Sequence);
        for (var i = 0; i < Versions.Count; i++)
            if (Versions[i].Manifest.Id == version.Manifest.Id && (Versions[i].Status == "Active" || Versions[i].Sequence == version.Sequence))
                Versions[i] = Versions[i] with { Status = Versions[i].Sequence == active ? "Active" : "Superseded" };
        return Task.CompletedTask;
    }
    public Task FailAsync(PluginVersion version, string error, CancellationToken ct)
    {
        var index = Versions.FindIndex(x => x.Sequence == version.Sequence);
        if (Versions[index].Status == "Pending") Versions[index] = Versions[index] with { Status = "Failed", Error = error };
        return Task.CompletedTask;
    }
    public Task DeleteAsync(long sequence, CancellationToken ct)
    {
        var index = Versions.FindIndex(x => x.Sequence == sequence);
        if (index < 0) throw new PluginNotFoundException("Plugin version not found.");
        Versions[index] = Versions[index] with { Status = "Deleted", Error = null };
        return Task.CompletedTask;
    }
    public Task ReportAsync(PluginVersion version, string worker, string status, string? error, CancellationToken ct)
    {
        Workers.Add(new(worker, version.Manifest.Id, version.Manifest.Version, status, error, DateTime.UtcNow));
        return Task.CompletedTask;
    }
    public Task<IReadOnlyList<PluginWorkerStatus>> WorkersAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<PluginWorkerStatus>>(Workers);
}

internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = [];
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    { lock (Messages) Messages.Add(formatter(state, exception)); }
}

internal static class PluginFixtures
{
    public static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "HangfireDemo.sln"))) directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
        }
    }
    public static string Package(string version) => Path.Combine(RepoRoot, "artifacts", "plugins", "send-report-" + version + ".zip");
    public static JsonElement Parameters(int seconds = 0) => JsonSerializer.SerializeToElement(new { reportName = "test", recipient = "demo@example.com", simulationSeconds = seconds });
}
