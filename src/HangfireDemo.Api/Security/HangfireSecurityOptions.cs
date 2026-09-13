namespace HangfireDemo.Api.Security;

public sealed class HangfireSecurityOptions
{
    public const string SectionName = "Security";

    public string HeaderName { get; init; } = "X-Hangfire-Api-Key";

    public string? AdminApiKey { get; init; }

    public string? ReaderApiKey { get; init; }
}
