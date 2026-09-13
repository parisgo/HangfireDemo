using HangfireDemo.Core.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HangfireDemo.Tests.Plugins;

public sealed class ApplicationLoggingTests
{
    [Fact]
    public async Task Seq_ReceivesStructuredEventsAndApiKey()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hangfire-seq-" + Guid.NewGuid().ToString("N"));
        using var diagnostics = new StringWriter();
        Seq.Extensions.Logging.SelfLog.Enable(TextWriter.Synchronized(diagnostics));
        var host = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder();
        host.Configuration["AllowedHosts"] = "127.0.0.1;localhost";
        host.Logging.ClearProviders();
        await using var server = host.Build();
        server.Urls.Add("http://127.0.0.1:0");
        var received = new TaskCompletionSource<(string Body, string Key)>(TaskCreationOptions.RunContinuationsAsynchronously);
        Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapPost(server, "/{**path}", async (Microsoft.AspNetCore.Http.HttpContext context) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            received.TrySetResult((await reader.ReadToEndAsync(), context.Request.Headers["X-Seq-ApiKey"].ToString()));
            context.Response.StatusCode = 201;
        });
        try
        {
            await server.StartAsync();
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Logging:File:Directory"] = directory,
                ["Logging:Seq:Enabled"] = "true",
                ["Logging:Seq:ServerUrl"] = server.Urls.Single(),
                ["Logging:Seq:ApiKey"] = "test-only-key"
            }).Build();
            using (var factory = LoggerFactory.Create(builder => builder.AddApplicationLogging(config, directory, "Worker")))
            {
                var logger = factory.CreateLogger("PluginJobRunner");
                using var scope = logger.BeginScope(new Dictionary<string, object?> { ["PluginId"] = "reports" });
                logger.LogInformation("Report {ReportName} completed", "monthly");
                var completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(10)));
                Assert.True(completed == received.Task, diagnostics.ToString());
                var result = await received.Task;
                Assert.Equal("test-only-key", result.Key);
                Assert.Contains("reports", result.Body);
                Assert.Contains("monthly", result.Body);
                Assert.Contains("ReportName", result.Body);
            }
        }
        finally
        {
            Seq.Extensions.Logging.SelfLog.Disable();
            await server.StopAsync();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void HostAndPluginLogs_IncludeScopesAndExceptions_AndRespectLevels()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hangfire-logs-" + Guid.NewGuid().ToString("N"));
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Logging:File:Directory"] = directory,
                ["Logging:LogLevel:Default"] = "Information"
            }).Build();
            using (var factory = LoggerFactory.Create(builder =>
            {
                builder.AddConfiguration(configuration.GetSection("Logging"));
                builder.AddApplicationLogging(configuration, directory, "Worker");
            }))
            {
                var logger = factory.CreateLogger("PluginJobRunner");
                using var scope = logger.BeginScope(new Dictionary<string, object?> { ["PluginId"] = "reports", ["HangfireJobId"] = "42" });
                logger.LogInformation("报表完成：{ReportName}", "月报");
                logger.LogDebug("hidden-debug-message");
                logger.LogError(new InvalidOperationException("example-error"), "Task failed");
            }
            var text = File.ReadAllText(Assert.Single(Directory.GetFiles(directory, "Worker-*.log")));
            Assert.Contains("月报", text);
            Assert.Contains("reports", text);
            Assert.Contains("42", text);
            Assert.Contains("PluginJobRunner", text);
            Assert.Contains("InvalidOperationException", text);
            Assert.Contains("example-error", text);
            Assert.DoesNotContain("hidden-debug-message", text);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }
}
