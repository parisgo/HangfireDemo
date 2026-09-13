using HangfireDemo.Core.Plugins;

namespace HangfireDemo.Tests.Plugins;

public sealed class PluginExecutionTests
{
    [Theory]
    [InlineData("Enqueued", false, "Fire-and-forget")]
    [InlineData("Scheduled", false, "Delayed")]
    [InlineData("Enqueued", true, "Recurring")]
    [InlineData("Scheduled", true, "Recurring")]
    [InlineData(null, true, "Recurring")]
    [InlineData(null, false, "Unknown")]
    public void TaskType_UsesOriginalStateAndRecurringOrigin(string? initialState, bool recurring, string expected)
        => Assert.Equal(expected, PluginExecutions.ResolveTaskType(initialState, recurring));

    [Theory]
    [InlineData("Processing", false)]
    [InlineData("Enqueued", false)]
    [InlineData("Awaiting", false)]
    [InlineData("Unknown", false)]
    [InlineData("Succeeded", true)]
    [InlineData("Failed", true)]
    [InlineData("Scheduled", true)]
    [InlineData("Deleted", true)]
    public void RestartPolicy_PreventsDuplicateActiveExecution(string state, bool expected)
        => Assert.Equal(expected, PluginExecutions.CanRestart(state));
}
