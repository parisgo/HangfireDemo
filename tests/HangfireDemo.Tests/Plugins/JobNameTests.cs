using Hangfire.Common;
using Hangfire.Storage;
using HangfireDemo.Core.Plugins;
using HangfireDemo.Core.WebApi;

namespace HangfireDemo.Tests.Plugins;

public sealed class JobNameTests
{
    [Fact]
    public void NamesSurviveHangfireSerialization_AndOldSignaturesStillResolve()
    {
        Job[] jobs = [
            Job.FromExpression<PluginJobRunner>(r => r.ExecuteAsync("reports", "send-report", "{}", "user", null, null, CancellationToken.None, "Monthly report")),
            Job.FromExpression<WebApiJobRunner>(r => r.ExecuteAsync("https://localhost/weather", "user", null, CancellationToken.None, "Weather check")),
            Job.FromExpression<PluginJobRunner>(r => r.ExecuteAsync("reports", "send-report", "{}", "user", null, null, CancellationToken.None)),
            Job.FromExpression<WebApiJobRunner>(r => r.ExecuteAsync("https://localhost/weather", "user", null, CancellationToken.None))
        ];
        string?[] expected = ["Monthly report", "Weather check", null, null];
        for (var i = 0; i < jobs.Length; i++)
        {
            var restored = InvocationData.SerializeJob(jobs[i]).DeserializeJob();
            Assert.Equal(expected[i], PluginExecutions.JobName(restored));
            Assert.Equal(jobs[i].Method, restored.Method);
        }
    }
}
