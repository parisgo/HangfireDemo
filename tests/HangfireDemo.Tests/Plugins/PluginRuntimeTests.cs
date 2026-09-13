using System.IO.Compression;
using HangfireDemo.Core.Plugins;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HangfireDemo.Tests.Plugins;

public sealed class PluginRuntimeTests
{
    // Loaded assemblies are intentionally retained until process exit, as in the Worker.
    private readonly string root = Path.Combine(PluginFixtures.RepoRoot, "artifacts", "plugin-tests", Guid.NewGuid().ToString("N"));
    private readonly MemoryPluginStore store = new();

    private async Task Install(string version, Action<ZipArchive>? edit = null)
    {
        var options = Options.Create(new PluginOptions { Directory = root });
        using var stream = new MemoryStream();
        await using (var file = File.OpenRead(PluginFixtures.Package(version))) await file.CopyToAsync(stream);
        if (edit is not null) { stream.Position = 0; using var archive = new ZipArchive(stream, ZipArchiveMode.Update, true); edit(archive); }
        stream.Position = 0;
        await new PluginPackageInstaller(store, options).InstallAsync(stream, default);
    }

    [Fact]
    public async Task Upgrade_UsesNewVersionForNextAttempt_ButKeepsRunningInstance()
    {
        await Install("1.0.0");
        var options = Options.Create(new PluginOptions { Directory = root });
        var runtime = new PluginRuntime(store, options);
        var watcher = new PluginWatcher(store, runtime, NullLogger<PluginWatcher>.Instance);
        await watcher.ScanAsync(default);
        var old = await runtime.CreateAsync("reports", "send-report", default);
        var logger = new RecordingLogger<PluginJobRunner>();
        var running = old.Job.ExecuteAsync(new("42", "batch", "user", logger), PluginFixtures.Parameters(1), default);
        await Install("2.0.0");
        await watcher.ScanAsync(default);
        var current = await runtime.CreateAsync("reports", "send-report", default);
        Assert.Equal("2.0.0", current.Version);
        await running;
        Assert.Contains(logger.Messages, x => x.Contains("v1.0.0 执行完成") && x.Contains("任务 42") && x.Contains("批次 batch") && x.Contains("耗时"));

        var runner = new PluginJobRunner(runtime, logger);
        await runner.ExecuteAsync("reports", "send-report", PluginFixtures.Parameters().GetRawText(), "user", "batch", null, default);
        Assert.Contains(logger.Messages, x => x.Contains("v2.0.0 执行完成"));
        var restarted = new PluginRuntime(store, options);
        Assert.Equal("2.0.0", (await restarted.CreateAsync("reports", "send-report", default)).Version);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BrokenUpgrade_PreservesActiveVersion(bool missingDependency)
    {
        await Install("1.0.0");
        var runtime = new PluginRuntime(store, Options.Create(new PluginOptions { Directory = root }));
        var watcher = new PluginWatcher(store, runtime, NullLogger<PluginWatcher>.Instance);
        await watcher.ScanAsync(default);
        await Install("2.0.0", archive =>
        {
            if (missingDependency) archive.GetEntry("ReportFormatting.dll")!.Delete();
            else
            {
                var entry = archive.GetEntry("plugin.json")!;
                string json;
                using (var reader = new StreamReader(entry.Open())) json = reader.ReadToEnd();
                entry.Delete();
                using var writer = new StreamWriter(archive.CreateEntry("plugin.json").Open());
                writer.Write(json.Replace("SendReport.Plugin.SendReport", "SendReport.Plugin.DoesNotExist"));
            }
        });
        await watcher.ScanAsync(default);
        Assert.Equal("1.0.0", (await store.ActiveAsync("reports", default))!.Manifest.Version);
        Assert.Equal("Failed", store.Versions[1].Status);
        Assert.NotEmpty(store.Versions[1].Error!);
    }

    [Fact]
    public async Task UploadedWhileOffline_RemainsPendingUntilWorkerScan_AndDuplicateRejected()
    {
        await Install("1.0.0");
        Assert.Equal("Pending", store.Versions.Single().Status);
        Assert.Null(await store.ActiveAsync("reports", default));
        await Assert.ThrowsAsync<PluginConflictException>(() => Install("1.0.0"));
        var runtime = new PluginRuntime(store, Options.Create(new PluginOptions { Directory = root }));
        await new PluginWatcher(store, runtime, NullLogger<PluginWatcher>.Instance).ScanAsync(default);
        Assert.Equal("1.0.0", (await store.ActiveAsync("reports", default))!.Manifest.Version);
        await Assert.ThrowsAsync<PluginValidationException>(() => runtime.CreateAsync("reports", "unknown", default));
    }
}
