using System.IO.Compression;
using System.Reflection.PortableExecutable;
using Microsoft.Extensions.Options;

namespace HangfireDemo.Core.Plugins;

public sealed class PluginPackageInstaller(IPluginStore store, IOptions<PluginOptions> options)
{
    public async Task<PluginManifest> InstallAsync(Stream upload, CancellationToken ct)
    {
        var settings = options.Value;
        var root = Path.GetFullPath(settings.Directory);
        Directory.CreateDirectory(root);
        for (var parent = new DirectoryInfo(root); parent is not null; parent = parent.Parent)
            if ((parent.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new PluginValidationException("Plugin directory cannot contain filesystem links.");
        var staging = Path.Combine(root, ".upload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        string? installed = null;
        try
        {
            var zipPath = Path.Combine(staging, "upload.zip");
            await using (var file = File.Create(zipPath))
                await CopyLimitedAsync(upload, file, settings.MaxUploadBytes, ct);
            var extracted = Path.Combine(staging, "files");
            Directory.CreateDirectory(extracted);
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                if (archive.Entries.Count > 2000) throw new PluginValidationException("Too many archive entries.");
                long total = 0;
                var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in archive.Entries)
                {
                    ct.ThrowIfCancellationRequested();
                    var name = entry.FullName.Replace('\\', '/');
                    if (name.StartsWith('/') || name.Contains(':') || name.Split('/').Any(x =>
                            x is ".." or "." || x != x.TrimEnd(' ', '.') ||
                            System.Text.RegularExpressions.Regex.IsMatch(x, "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\\.|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) ||
                        ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 ||
                        (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                        throw new PluginValidationException("Archive paths and links are not allowed.");
                    var destination = Path.GetFullPath(Path.Combine(extracted, name));
                    if (!destination.StartsWith(extracted + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !paths.Add(destination))
                        throw new PluginValidationException("Invalid or duplicate archive path.");
                    if (name.EndsWith('/')) { Directory.CreateDirectory(destination); continue; }
                    if (entry.Length > settings.MaxExtractedBytes - total)
                        throw new PluginValidationException("Expanded package exceeds its size limit.");
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    await using var source = entry.Open();
                    await using var target = File.Create(destination);
                    total += await CopyLimitedAsync(source, target, settings.MaxExtractedBytes - total, ct);
                }
            }
            var manifestPath = Path.Combine(extracted, "plugin.json");
            if (!File.Exists(manifestPath) || new FileInfo(manifestPath).Length > 256 * 1024)
                throw new PluginValidationException("A plugin.json of at most 256 KB is required at the package root.");
            var manifest = PluginValidation.ReadManifest(await File.ReadAllTextAsync(manifestPath, ct));
            if (!File.Exists(Path.Combine(extracted, manifest.EntryAssembly)) ||
                !File.Exists(Path.Combine(extracted, Path.ChangeExtension(manifest.EntryAssembly, ".deps.json"))))
                throw new PluginValidationException("Entry DLL and its .deps.json are required.");
            foreach (var path in Directory.EnumerateFiles(extracted, "*", SearchOption.AllDirectories))
            {
                var extension = Path.GetExtension(path).ToLowerInvariant();
                if (extension is ".so" or ".dylib" or ".exe")
                    throw new PluginValidationException("Only managed DLL plugins are supported.");
                if (extension != ".dll") continue;
                using var file = File.OpenRead(path);
                using var pe = new PEReader(file);
                if (!pe.HasMetadata || pe.PEHeaders.CorHeader is null ||
                    !pe.PEHeaders.CorHeader.Flags.HasFlag(CorFlags.ILOnly))
                    throw new PluginValidationException("Native or mixed-mode DLLs are not supported.");
            }
            installed = Path.Combine(root, manifest.Id + "-" + manifest.Version + "-" + Guid.NewGuid().ToString("N"));
            Directory.Move(extracted, installed);
            await store.AddAsync(manifest, installed, ct);
            return manifest;
        }
        catch (PluginConflictException)
        {
            if (installed is not null) Directory.Delete(installed, true);
            throw;
        }
        catch (Exception ex) when (ex is InvalidDataException or BadImageFormatException)
        { throw new PluginValidationException("Invalid ZIP or managed assembly: " + ex.Message); }
        finally { Directory.Delete(staging, true); }
    }

    private static async Task<long> CopyLimitedAsync(Stream source, Stream target, long limit, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        int count;
        while ((count = await source.ReadAsync(buffer, ct)) > 0)
        {
            total += count;
            if (total > limit) throw new PluginValidationException("Package exceeds its size limit.");
            await target.WriteAsync(buffer.AsMemory(0, count), ct);
        }
        return total;
    }
}
