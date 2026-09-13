using Hangfire;
using Hangfire.SqlServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HangfireDemo.Core.Jobs.Configuration;

public static class HangfireServiceCollectionExtensions
{
    public static IServiceCollection AddHangfirePersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("HangfireConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'HangfireConnection' is required.");
        }

        var settings = configuration
            .GetSection(HangfireSettings.SectionName)
            .Get<HangfireSettings>() ?? new HangfireSettings();

        services.AddOptions<HangfireSettings>()
            .Bind(configuration.GetSection(HangfireSettings.SectionName))
            .Validate(
                settings => settings.Queues.Length > 0,
                "At least one Hangfire queue must be configured.")
            .Validate(
                settings => settings.WorkerCount >= 0,
                "Hangfire WorkerCount cannot be negative.")
            .Validate(
                settings => settings.ServerHeartbeatTimeoutSeconds >= 30,
                "Hangfire heartbeat timeout must be at least 30 seconds.")
            .ValidateOnStart();

        services.AddHangfire(hangfire => hangfire
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseSqlServerStorage(connectionString, new SqlServerStorageOptions
            {
                CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
                SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
                QueuePollInterval = TimeSpan.Zero,
                UseRecommendedIsolationLevel = true,
                DisableGlobalLocks = true,
                TryAutoDetectSchemaDependentOptions = false,
                PrepareSchemaIfNecessary = settings.PrepareSchemaIfNecessary
            }));

        return services;
    }
}
