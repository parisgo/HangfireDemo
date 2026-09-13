using System.Text.Json;
using System.Diagnostics;
using HangfireDemo.Core.Plugins;
using Microsoft.Extensions.Logging;

namespace SendReport.Plugin;

public sealed class SendReport : IPluginJob
{
    public async Task ExecuteAsync(JobExecutionContext context, JsonElement parameters, CancellationToken cancellationToken)
    {
        var reportName = ReportFormatting.ReportLabel.Format(RequiredString(parameters, "reportName"));
        var recipient = RequiredString(parameters, "recipient");
        var seconds = parameters.TryGetProperty("simulationSeconds", out var duration) ? duration.GetInt32() : 1;
        if (seconds is < 0 or > 30) throw new ArgumentException("simulationSeconds must be between 0 and 30.");
        var version = typeof(SendReport).Assembly.GetName().Version!.ToString(3);
        if (parameters.TryGetProperty("failOnVersion", out var failOnVersion) && failOnVersion.GetString() == version)
            throw new InvalidOperationException("Simulated failure on version " + version);
        context.Logger.LogInformation("报表插件 v{Version} 开始执行：{ReportName}，接收人 {Recipient}，任务 {HangfireJobId}，批次 {BatchId}",
            version, reportName, recipient, context.JobId, context.BatchId);
        var stopwatch = Stopwatch.StartNew();
        await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken);
        context.Logger.LogInformation("报表插件 v{Version} 执行完成：{ReportName}，任务 {HangfireJobId}，批次 {BatchId}，耗时 {ElapsedMilliseconds} ms。仅模拟执行，未发送邮件。",
            version, reportName, context.JobId, context.BatchId, stopwatch.ElapsedMilliseconds);
    }

    private static string RequiredString(JsonElement parameters, string name)
    {
        if (!parameters.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString())) throw new ArgumentException(name + " is required.");
        return value.GetString()!;
    }
}
