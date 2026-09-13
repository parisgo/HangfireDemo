using System.Security.Claims;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using HangfireDemo.Api.Controllers;
using HangfireDemo.Core.Jobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace HangfireDemo.Tests.Jobs;

public sealed class JobsControllerTests
{
    [Theory]
    [InlineData("import-commandes", typeof(ImportCommandeJob), "imports")]
    [InlineData("send-report", typeof(SendReportJob), "default")]
    public void Enqueue_RegisteredJob_PreservesArgumentsAndQueue(string name, Type type, string queue)
    {
        var client = new RecordingClient();
        var controller = CreateController(client);
        var batchId = Guid.NewGuid();

        Assert.IsType<AcceptedResult>(controller.Enqueue(name, batchId));

        Assert.NotNull(client.Job);
        Assert.Equal(type, client.Job.Type);
        Assert.Equal(batchId.ToString("N"), client.Job.Args[0]);
        Assert.Equal("operator", client.Job.Args[1]);
        Assert.Equal(queue, Assert.IsType<EnqueuedState>(client.State).Queue);
    }

    [Theory]
    [InlineData("import-commandes")]
    [InlineData("send-report")]
    public void Schedule_RegisteredJob_CreatesDelayedState(string name)
    {
        var client = new RecordingClient();
        var before = DateTime.UtcNow.AddMinutes(5);
        Assert.IsType<AcceptedResult>(CreateController(client).Schedule(name, 5));
        Assert.InRange(Assert.IsType<ScheduledState>(client.State).EnqueueAt,
            before, DateTime.UtcNow.AddMinutes(5));
        Assert.NotNull(client.Job);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1441)]
    public void Schedule_InvalidDelay_DoesNotSubmit(int minutes)
    {
        var client = new RecordingClient();
        Assert.IsType<BadRequestObjectResult>(CreateController(client).Schedule("send-report", minutes));
        Assert.Null(client.Job);
    }

    [Fact]
    public void UnknownJob_IsRejectedByAllSubmissionRoutes()
    {
        var client = new RecordingClient();
        var controller = CreateController(client);
        Assert.IsType<NotFoundObjectResult>(controller.Enqueue("System.Console"));
        Assert.IsType<NotFoundObjectResult>(controller.Schedule("unknown"));
        Assert.IsType<NotFoundObjectResult>(controller.TriggerRecurring("unknown"));
        Assert.Null(client.Job);
    }

    [Fact]
    public void TriggerRecurring_JobWithoutSchedule_IsRejected()
    {
        Assert.IsType<BadRequestObjectResult>(CreateController(new RecordingClient())
            .TriggerRecurring("send-report"));
    }

    [Fact]
    public async Task Report_CanBeActivatedAndExecutedWithoutBusinessDatabase()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddJobExecution();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<SendReportJob>();
        await job.ExecuteAsync("test-batch", "test-user", CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            job.ExecuteAsync("cancelled-batch", "test-user", cancellation.Token));
    }

    private static JobsController CreateController(RecordingClient client) => new(client, null!, new JobCatalog())
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.Name, "operator")], "test"))
            }
        }
    };

    private sealed class RecordingClient : IBackgroundJobClient
    {
        public Job? Job { get; private set; }
        public IState? State { get; private set; }

        public string Create(Job job, IState state)
        {
            Job = job;
            State = state;
            return "test-job-id";
        }

        public bool ChangeState(string jobId, IState state, string expectedState)
            => throw new NotSupportedException();
    }
}
