using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HangfireDemo.Core.Plugins;

public sealed record LoadedPlugin(PluginVersion Version, IReadOnlyDictionary<string, Type> Jobs);

public sealed class PluginRuntime(IPluginStore store, IOptions<PluginOptions> options)
{
    private readonly ConcurrentDictionary<long, Lazy<LoadedPlugin>> loaded = new();

    public LoadedPlugin Load(PluginVersion version) => loaded.GetOrAdd(version.Sequence,
        _ => new Lazy<LoadedPlugin>(() => LoadCore(version))).Value;

    private LoadedPlugin LoadCore(PluginVersion version)
    {
        var root = Path.GetFullPath(options.Value.Directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var directory = Path.GetFullPath(version.Directory);
        if (!directory.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new PluginValidationException("Plugin path is outside the shared directory.");
        var context = new PluginLoadContext(Path.Combine(directory, version.Manifest.EntryAssembly));
        var assembly = context.LoadFromAssemblyPath(Path.Combine(directory, version.Manifest.EntryAssembly));
        if (assembly.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>()?.FrameworkName != ".NETCoreApp,Version=v8.0")
            throw new PluginValidationException("Plugin entry assembly must target .NET 8.");
        // Resolve all managed references before activation, including dependencies of dependencies.
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void ValidateReferences(Assembly candidate)
        {
            if (!visited.Add(candidate.FullName!)) return;
            foreach (var reference in candidate.GetReferencedAssemblies())
            {
                var dependency = context.LoadFromAssemblyName(reference);
                if (AssemblyLoadContext.GetLoadContext(dependency) == context) ValidateReferences(dependency);
            }
        }
        ValidateReferences(assembly);
        var types = new Dictionary<string, Type>();
        foreach (var task in version.Manifest.Jobs)
        {
            var type = assembly.GetType(task.Type, throwOnError: true)!;
            if (!typeof(IPluginJob).IsAssignableFrom(type) || type.IsAbstract || !type.IsPublic ||
                type.ContainsGenericParameters || type.GetConstructor(Type.EmptyTypes) is null)
                throw new PluginValidationException($"{task.Type} must be a public IPluginJob with a parameterless constructor.");
            types.Add(task.Id, type);
        }
        return new(version, types);
    }

    public async Task<(IPluginJob Job, string Version)> CreateAsync(string pluginId, string jobId, CancellationToken ct)
    {
        var version = await store.ActiveAsync(pluginId, ct)
            ?? throw new PluginValidationException("Plugin has no active version: " + pluginId);
        var plugin = Load(version);
        if (!plugin.Jobs.TryGetValue(jobId, out var type))
            throw new PluginValidationException("The active plugin no longer provides task " + jobId);
        return ((IPluginJob)Activator.CreateInstance(type)!, version.Manifest.Version);
    }

    private sealed class PluginLoadContext(string entryPath) : AssemblyLoadContext(isCollectible: false)
    {
        private readonly AssemblyDependencyResolver resolver = new(entryPath);
        private static readonly HashSet<string> FrameworkAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "")
            .Split(Path.PathSeparator).Where(path => path.Contains(Path.DirectorySeparatorChar + "shared" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetFileNameWithoutExtension(path)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        protected override Assembly? Load(AssemblyName name)
        {
            if (name.Name == typeof(IPluginJob).Assembly.GetName().Name)
                return typeof(IPluginJob).Assembly;
            if (FrameworkAssemblies.Contains(name.Name!)) return Default.LoadFromAssemblyName(name);
            var path = resolver.ResolveAssemblyToPath(name);
            if (path is null) throw new FileNotFoundException("Missing plugin dependency: " + name.FullName);
            return LoadFromAssemblyPath(path);
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
            => throw new NotSupportedException("Native plugin dependencies are not supported.");
    }
}

public sealed class PluginWatcher(IPluginStore store, PluginRuntime runtime, ILogger<PluginWatcher> logger) : BackgroundService
{
    private readonly string workerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    public async Task ScanAsync(CancellationToken ct)
    {
        foreach (var version in await store.ListAsync(ct))
        {
            if (version.Status is not ("Pending" or "Active")) continue;
            try
            {
                runtime.Load(version);
                if (version.Status == "Pending") await store.ActivateAsync(version, ct);
                await store.ReportAsync(version, workerId, "Loaded", null, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Cannot load plugin {PluginId} {Version}", version.Manifest.Id, version.Manifest.Version);
                await store.FailAsync(version, ex.Message, ct);
                await store.ReportAsync(version, workerId, "Failed", ex.Message, ct);
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ScanAsync(stoppingToken); }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            { logger.LogError(ex, "Plugin scan failed; retrying in five seconds"); }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
