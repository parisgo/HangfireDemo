using HangfireDemo.Core.Jobs.Configuration;

namespace HangfireDemo.Tests.Configuration;

public sealed class HangfireSettingsTests
{
    [Fact]
    public void Defaults_ConsumeControlAndImportQueues()
    {
        var settings = new HangfireSettings();

        Assert.Contains(HangfireQueues.Default, settings.Queues);
        Assert.Contains(HangfireQueues.Imports, settings.Queues);
        Assert.Equal("0 2 * * *", settings.DailyImportCron);
    }
}
