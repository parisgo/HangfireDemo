namespace HangfireDemo.Core.Jobs.Configuration;

public static class TimeZoneResolver
{
    public static TimeZoneInfo Resolve(string configuredId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuredId);

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(configuredId);
        }
        catch (TimeZoneNotFoundException) when (
            OperatingSystem.IsWindows() &&
            string.Equals(configuredId, "Europe/Paris", StringComparison.OrdinalIgnoreCase))
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time");
        }
    }
}
