using HangfireDemo.Core.Jobs.Configuration;

namespace HangfireDemo.Tests.Configuration;

public sealed class TimeZoneResolverTests
{
    [Fact]
    public void Resolve_ParisTimeZone_UsesExpectedWinterAndSummerOffsets()
    {
        var timeZone = TimeZoneResolver.Resolve("Europe/Paris");

        var winterOffset = timeZone.GetUtcOffset(
            new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Unspecified));
        var summerOffset = timeZone.GetUtcOffset(
            new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Unspecified));

        Assert.Equal(TimeSpan.FromHours(1), winterOffset);
        Assert.Equal(TimeSpan.FromHours(2), summerOffset);
    }

    [Fact]
    public void Resolve_EmptyId_Throws()
    {
        Assert.Throws<ArgumentException>(() => TimeZoneResolver.Resolve(""));
    }
}
