using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace HangfireDemo.Core.Plugins;

public interface IPluginJob
{
    Task ExecuteAsync(JobExecutionContext context, JsonElement parameters, CancellationToken cancellationToken);
}

public sealed record JobExecutionContext(string JobId, string BatchId, string RequestedBy, ILogger Logger)
{
    // Plugins select a configured connection by name; credentials never enter job arguments.
    public Func<string, System.Data.Common.DbConnection> CreateConnection { get; init; } =
        _ => throw new InvalidOperationException("No connection factory is available in this execution context.");
}
public sealed record PluginTask(string Id, string Name, string Type, JsonElement ParametersExample);
public sealed record PluginManifest(string Id, string Version, string EntryAssembly, PluginTask[] Jobs);
public sealed record PluginVersion(long Sequence, PluginManifest Manifest, string Directory, string Status, string? Error);
public sealed record PluginWorkerStatus(string WorkerId, string PluginId, string Version, string Status, string? Error, DateTime HeartbeatUtc);

public sealed class PluginOptions
{
    public string Directory { get; set; } = "";
    public long MaxUploadBytes { get; set; } = 50 * 1024 * 1024;
    public long MaxExtractedBytes { get; set; } = 200 * 1024 * 1024;
}

public sealed class PluginValidationException(string message) : Exception(message);
public sealed class PluginConflictException(string message) : Exception(message);

public static class PluginValidation
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static void Identifier(string? value)
    {
        if (value is null || !Regex.IsMatch(value, "^[a-z0-9][a-z0-9-]{0,63}$"))
            throw new PluginValidationException("ID must contain 1–64 lowercase letters, digits or hyphens.");
    }

    public static void Parameters(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new PluginValidationException("Parameters must be a JSON object.");
        if (value.GetRawText().Length > 64 * 1024)
            throw new PluginValidationException("Parameters cannot exceed 64 KB.");
    }

    public static PluginManifest ReadManifest(string json)
    {
        PluginManifest manifest;
        try { manifest = JsonSerializer.Deserialize<PluginManifest>(json, Json) ?? throw new JsonException(); }
        catch (JsonException) { throw new PluginValidationException("Invalid plugin.json."); }
        Identifier(manifest.Id);
        if (manifest.Version is null || !Regex.IsMatch(manifest.Version, "^[0-9]+\\.[0-9]+\\.[0-9]+(?:-[a-zA-Z0-9.-]+)?$") || manifest.Version.Length > 64)
            throw new PluginValidationException("Version must be a semantic version, e.g. 1.0.0.");
        if (string.IsNullOrWhiteSpace(manifest.EntryAssembly) ||
            manifest.EntryAssembly != Path.GetFileName(manifest.EntryAssembly) ||
            manifest.EntryAssembly.Contains(':') || manifest.EntryAssembly.Contains('\\') ||
            !manifest.EntryAssembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            throw new PluginValidationException("EntryAssembly must be a DLL filename at the package root.");
        if (manifest.Jobs is null || manifest.Jobs.Length is < 1 or > 100)
            throw new PluginValidationException("Declare between 1 and 100 jobs.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var job in manifest.Jobs)
        {
            if (job is null) throw new PluginValidationException("Invalid job declaration.");
            Identifier(job.Id);
            if (!ids.Add(job.Id)) throw new PluginValidationException("Duplicate task ID.");
            if (string.IsNullOrWhiteSpace(job.Name) || job.Name.Length > 200 || string.IsNullOrWhiteSpace(job.Type))
                throw new PluginValidationException("Every task needs a name and implementation type.");
            Parameters(job.ParametersExample);
        }
        return manifest;
    }
}
