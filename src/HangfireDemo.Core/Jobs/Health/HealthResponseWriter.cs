using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HangfireDemo.Core.Jobs.Health;

public static class HealthResponseWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        return context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            durationMilliseconds = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description,
                data = entry.Value.Data
            })
        }));
    }
}
