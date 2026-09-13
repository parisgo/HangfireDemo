using System.Diagnostics;
using Hangfire;
using Hangfire.Server;
using Microsoft.Extensions.Logging;
using HangfireDemo.Core.Plugins;

namespace HangfireDemo.Core.WebApi;

public static class WebApiValidation
{
    public static string Url(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048 ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || string.IsNullOrEmpty(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
            throw new PluginValidationException("Enter an absolute HTTP or HTTPS URL without credentials or a fragment.");
        return uri.AbsoluteUri;
    }
}

[AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 60, 120])]
public sealed class WebApiJobRunner(IHttpClientFactory clients, ILogger<WebApiJobRunner> logger)
{
    [Queue("default")]
    public async Task ExecuteAsync(string url, string requestedBy, PerformContext? context, CancellationToken cancellationToken, string? name)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object?> { ["JobName"] = name });
        await ExecuteAsync(url, requestedBy, context, cancellationToken);
    }

    [Queue("default")]
    public async Task ExecuteAsync(string url, string requestedBy, PerformContext? context, CancellationToken cancellationToken)
    {
        url = WebApiValidation.Url(url);
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["HangfireJobId"] = context?.BackgroundJob.Id, ["RequestedBy"] = requestedBy, ["JobKind"] = "WebAPI"
        });
        var stopwatch = Stopwatch.StartNew();
        logger.LogInformation("WebAPI GET started: {Url}", url);
        using var client = clients.CreateClient("WebApiJobs");
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        logger.LogInformation("WebAPI GET completed: {Url}, status {StatusCode}, elapsed {ElapsedMilliseconds} ms",
            url, (int)response.StatusCode, stopwatch.ElapsedMilliseconds);
        response.EnsureSuccessStatusCode();
    }
}
