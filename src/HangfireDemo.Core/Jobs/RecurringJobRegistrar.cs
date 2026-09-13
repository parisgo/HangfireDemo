using Hangfire;
using HangfireDemo.Core.Jobs.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HangfireDemo.Core.Jobs;

public sealed class RecurringJobRegistrar(
    IRecurringJobManager recurringJobs,
    IOptions<HangfireSettings> settings,
    ILogger<RecurringJobRegistrar> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                Register();
                return;
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(
                    exception,
                    "Unable to register recurring jobs; retrying in 15 seconds");

                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
        }
    }

    private void Register()
    {
        var options = settings.Value;
        var timeZone = TimeZoneResolver.Resolve(options.DailyImportTimeZone);

        recurringJobs.AddOrUpdate<ImportCommandeJob>(
            HangfireJobIds.DailyCommandeImport,
            job => job.ExecuteAsync(null, "scheduler", null, CancellationToken.None),
            options.DailyImportCron,
            new RecurringJobOptions
            {
                TimeZone = timeZone
            });

        logger.LogInformation(
            "Registered recurring job {RecurringJobId} with cron {Cron} in time zone {TimeZone}",
            HangfireJobIds.DailyCommandeImport,
            options.DailyImportCron,
            timeZone.Id);
    }
}
