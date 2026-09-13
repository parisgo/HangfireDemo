using HangfireDemo.Core.Jobs.Configuration;

namespace HangfireDemo.Tests.Configuration;

public sealed class HangfireSettingsTests
{
    [Fact]
    public void Defaults_ConsumeOnlyTheGenericQueue()
    {
        var settings = new HangfireSettings();

        Assert.Equal([HangfireQueues.Default], settings.Queues);
    }
}
