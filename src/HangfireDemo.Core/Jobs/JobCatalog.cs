using System.Diagnostics.CodeAnalysis;
using Hangfire.Common;
using HangfireDemo.Core.Jobs.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HangfireDemo.Core.Jobs;

public sealed record JobDefinition(
    string Name,
    string Queue,
    string? RecurringJobId,
    Func<string, string, Job> CreateJob);

public sealed class JobCatalog
{
    // Only explicitly registered jobs can be submitted through the API.
    private readonly Dictionary<string, JobDefinition> definitions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["import-commandes"] = new(
            "import-commandes", HangfireQueues.Imports, HangfireJobIds.DailyCommandeImport,
            (batchId, requestedBy) => Job.FromExpression<ImportCommandeJob>(job =>
                job.ExecuteAsync(batchId, requestedBy, null, CancellationToken.None))),
        ["send-report"] = new(
            "send-report", HangfireQueues.Default, null,
            (batchId, requestedBy) => Job.FromExpression<SendReportJob>(job =>
                job.ExecuteAsync(batchId, requestedBy, CancellationToken.None)))
    };

    public IEnumerable<JobDefinition> Definitions => definitions.Values;

    public bool TryGet(string name, [NotNullWhen(true)] out JobDefinition? definition)
        => definitions.TryGetValue(name, out definition);
}

public static class JobRegistrationExtensions
{
    public static IServiceCollection AddJobCatalog(this IServiceCollection services)
        => services.AddSingleton<JobCatalog>();

    public static IServiceCollection AddJobExecution(this IServiceCollection services)
    {
        services.AddScoped<ImportCommandeJob>();
        services.AddScoped<SendReportJob>();
        return services;
    }
}
