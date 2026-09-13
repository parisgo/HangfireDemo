using System.Net;
using HangfireDemo.Core.Plugins;
using HangfireDemo.Core.WebApi;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HangfireDemo.Tests.Plugins;

public sealed class WebApiJobTests
{
    [Fact]
    public async Task WorkerClient_Follows307AndLimitsRedirectLoops()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["AllowedHosts"] = "localhost;127.0.0.1";
        builder.Logging.ClearProviders();
        builder.Services.AddWebApiExecution();
        await using var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        var hits = 0;
        app.MapGet("/redirect", () => Results.Redirect("/weather", preserveMethod: true));
        app.MapGet("/weather", () => { hits++; return Results.Ok(); });
        app.MapGet("/loop", () => Results.Redirect("/loop", preserveMethod: true));
        await app.StartAsync();
        try
        {
            using var scope = app.Services.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<WebApiJobRunner>();
            await runner.ExecuteAsync(app.Urls.Single() + "/redirect", "test", null, default);
            Assert.Equal(1, hits);
            await Assert.ThrowsAsync<HttpRequestException>(() => runner.ExecuteAsync(app.Urls.Single() + "/loop", "test", null, default));
        }
        finally { await app.StopAsync(); }
    }
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/WeatherForecast")]
    [InlineData("file:///c:/test")]
    [InlineData("https://user:password@localhost/test")]
    [InlineData("https://localhost/test#fragment")]
    public void InvalidUrlsAreRejected(string? url)
        => Assert.Throws<PluginValidationException>(() => WebApiValidation.Url(url));

    [Fact]
    public void LocalHttpsUrlIsSupported()
        => Assert.Equal("https://localhost:7062/WeatherForecast", WebApiValidation.Url("https://localhost:7062/WeatherForecast"));

    [Theory]
    [InlineData(HttpStatusCode.OK, false)]
    [InlineData(HttpStatusCode.NoContent, false)]
    [InlineData(HttpStatusCode.Found, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    public async Task RunnerUsesGetWithoutBodyAndFailsOnNonSuccess(HttpStatusCode status, bool fails)
    {
        var handler = new Handler(status);
        var runner = new WebApiJobRunner(new Factory(handler), NullLogger<WebApiJobRunner>.Instance);
        var execution = runner.ExecuteAsync("https://localhost:7062/WeatherForecast", "test", null, default);
        if (fails) await Assert.ThrowsAsync<HttpRequestException>(() => execution); else await execution;
        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Null(handler.Content);
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
    private sealed class Handler(HttpStatusCode status) : HttpMessageHandler
    {
        public HttpMethod? Method;
        public HttpContent? Content;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Method = request.Method; Content = request.Content;
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }
}
