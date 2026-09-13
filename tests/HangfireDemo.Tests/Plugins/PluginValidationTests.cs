using System.IO.Compression;
using System.Text.Json;
using Hangfire.States;
using HangfireDemo.Core.Plugins;
using Microsoft.Extensions.Options;

namespace HangfireDemo.Tests.Plugins;

public sealed class PluginValidationTests
{
    [Theory]
    [InlineData("../escape.dll")]
    [InlineData("nested/../../escape.dll")]
    [InlineData("/absolute.dll")]
    [InlineData("C:/escape.dll")]
    [InlineData("nested/.. /escape.dll")]
    [InlineData("NUL.dll")]
    public async Task Package_RejectsUnsafePaths(string name)
    {
        using var zip = Archive(name, "x");
        await Assert.ThrowsAsync<PluginValidationException>(() => Installer().InstallAsync(zip, default));
    }

    [Fact]
    public async Task Package_RejectsSymlink()
    {
        using var zip = Archive("link", "target", unchecked((int)0xA1FF0000));
        await Assert.ThrowsAsync<PluginValidationException>(() => Installer().InstallAsync(zip, default));
    }

    [Fact]
    public async Task Package_EnforcesCompressedAndExpandedLimits()
    {
        using var zip = Archive("file.txt", new string('a', 4096));
        await Assert.ThrowsAsync<PluginValidationException>(() => Installer(20, 10000).InstallAsync(zip, default));
        zip.Position = 0;
        await Assert.ThrowsAsync<PluginValidationException>(() => Installer(10000, 20).InstallAsync(zip, default));
    }

    [Fact]
    public async Task Package_RequiresManifest()
    {
        using var zip = Archive("readme.txt", "hello");
        await Assert.ThrowsAsync<PluginValidationException>(() => Installer().InstallAsync(zip, default));
    }

    [Theory]
    [InlineData("bad/id", "1.0.0", "Plugin.dll")]
    [InlineData("reports", "latest", "Plugin.dll")]
    [InlineData("reports", "1.0.0", "../Plugin.dll")]
    public void Manifest_RejectsInvalidIdentity(string id, string version, string entry)
    {
        var manifest = new PluginManifest(id, version, entry, [new("send-report", "Report", "Plugin.Report", PluginFixtures.Parameters())]);
        Assert.Throws<PluginValidationException>(() => PluginValidation.ReadManifest(JsonSerializer.Serialize(manifest, PluginValidation.Json)));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("42")]
    public void Parameters_RequireObject(string json)
        => Assert.Throws<PluginValidationException>(() => PluginValidation.Parameters(JsonSerializer.Deserialize<JsonElement>(json)));

    [Theory]
    [InlineData("Delayed", 0)]
    [InlineData("Delayed", 1441)]
    [InlineData("Delayed", null)]
    [InlineData("Fire-and-forget", 1)]
    [InlineData("invalid", null)]
    public void InvalidExecutionMode_IsRejected(string mode, int? minutes)
        => Assert.Throws<PluginValidationException>(() => PluginScheduling.ExecutionState(new(mode, PluginFixtures.Parameters(), minutes)));

    [Fact]
    public void ImmediateAndDelayed_UseCorrectState()
    {
        Assert.Equal("default", Assert.IsType<EnqueuedState>(PluginScheduling.ExecutionState(new("Fire-and-forget", PluginFixtures.Parameters()))).Queue);
        var start = DateTime.UtcNow.AddMinutes(2);
        var state = Assert.IsType<ScheduledState>(PluginScheduling.ExecutionState(new("Delayed", PluginFixtures.Parameters(), 2)));
        Assert.InRange(state.EnqueueAt, start, DateTime.UtcNow.AddMinutes(2));
    }

    [Theory]
    [InlineData("* * * * * *", "Europe/Paris")]
    [InlineData("90 * * * *", "Europe/Paris")]
    [InlineData("* * * * *", "Invalid/Timezone")]
    public void InvalidSchedule_IsRejected(string cron, string zone)
        => Assert.Throws<PluginValidationException>(() => PluginScheduling.ValidateSchedule(new("reports", "send-report", PluginFixtures.Parameters(), cron, zone)));

    [Fact]
    public void Schedule_DefaultsToParis()
        => Assert.NotNull(PluginScheduling.ValidateSchedule(new("reports", "send-report", PluginFixtures.Parameters(), "0 9 * * *")));

    private static PluginPackageInstaller Installer(long uploaded = 50000, long expanded = 50000) => new(new MemoryPluginStore(),
        Options.Create(new PluginOptions { Directory = Path.Combine(PluginFixtures.RepoRoot, "artifacts", "package-tests", Guid.NewGuid().ToString("N")),
            MaxUploadBytes = uploaded, MaxExtractedBytes = expanded }));

    private static MemoryStream Archive(string name, string content, int? attributes = null)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            var entry = zip.CreateEntry(name);
            if (attributes.HasValue) entry.ExternalAttributes = attributes.Value;
            using var writer = new StreamWriter(entry.Open()); writer.Write(content);
        }
        stream.Position = 0;
        return stream;
    }
}
