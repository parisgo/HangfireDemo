using Microsoft.Extensions.DependencyInjection;

namespace HangfireDemo.Core.WebApi;

public static class WebApiServices
{
    public static IServiceCollection AddWebApiExecution(this IServiceCollection services)
    {
        services.AddScoped<WebApiJobRunner>();
        services.AddHttpClient("WebApiJobs", client => client.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 5
            });
        return services;
    }
}
