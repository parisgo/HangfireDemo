using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HangfireDemo.Core.Plugins;

public static class PluginServices
{
    public static IServiceCollection AddPluginStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PluginOptions>().Bind(configuration.GetSection("Plugins"))
            .Validate(x => Path.IsPathFullyQualified(x.Directory), "Plugins:Directory must be the same absolute path in Api and Worker.")
            .Validate(x => x.MaxUploadBytes is > 0 and <= 50 * 1024 * 1024 && x.MaxExtractedBytes is > 0 and <= 200 * 1024 * 1024,
                "Plugin size limits must not exceed 50 MB uploaded / 200 MB expanded.").ValidateOnStart();
        services.AddSingleton<IPluginStore>(_ => new SqlPluginStore(configuration.GetConnectionString("HangfireConnection")
            ?? throw new InvalidOperationException("HangfireConnection is required.")));
        services.AddSingleton<PluginPackageInstaller>();
        return services;
    }

    public static IServiceCollection AddPluginExecution(this IServiceCollection services)
    {
        services.AddSingleton<PluginRuntime>();
        services.AddScoped<PluginJobRunner>();
        services.AddHostedService<PluginWatcher>();
        return services;
    }
}
