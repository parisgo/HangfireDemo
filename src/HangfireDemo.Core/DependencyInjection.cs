using HangfireDemo.Core.Commandes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HangfireDemo.Core;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("ApplicationConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = configuration.GetConnectionString("HangfireConnection");
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'ApplicationConnection' or 'HangfireConnection' is required.");
        }

        services.AddScoped<ICommandeImportService>(serviceProvider =>
            new SqlCommandeImportService(
                connectionString,
                serviceProvider.GetRequiredService<ILogger<SqlCommandeImportService>>()));

        return services;
    }
}
