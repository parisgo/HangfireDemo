namespace HangfireDemo.Core.Jobs.Configuration;

public sealed class HangfireSettings
{
    public const string SectionName = "Hangfire";

    public string[] Queues { get; init; } = [HangfireQueues.Default, HangfireQueues.Imports];

    public int WorkerCount { get; init; }

    public string DailyImportCron { get; init; } = "0 2 * * *";

    public string DailyImportTimeZone { get; init; } = "Europe/Paris";

    public int ServerHeartbeatTimeoutSeconds { get; init; } = 120;

    public bool PrepareSchemaIfNecessary { get; init; }
}
