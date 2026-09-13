namespace HangfireDemo.Core.Jobs.Configuration;

public sealed class HangfireSettings
{
    public const string SectionName = "Hangfire";

    public string[] Queues { get; init; } = [HangfireQueues.Default];

    public int WorkerCount { get; init; }

    public int ServerHeartbeatTimeoutSeconds { get; init; } = 120;

    public bool PrepareSchemaIfNecessary { get; init; }
}
