using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Extensions.Logging;

namespace HangfireDemo.Core.Logging;

public static class ApplicationLogging
{
    public static ILoggingBuilder AddApplicationLogging(this ILoggingBuilder logging,
        IConfiguration configuration, string contentRoot, string applicationName)
    {
        var section = configuration.GetSection("Logging:File");
        var directory = section["Directory"] ?? "Logs";
        var size = section.GetValue<long>("FileSizeLimitBytes", 50 * 1024 * 1024);
        var retained = section.GetValue<int>("RetainedFileCountLimit", 31);
        if (string.IsNullOrWhiteSpace(directory) || size <= 0 || retained <= 0)
            throw new InvalidOperationException("Logging:File requires a directory and positive size/retention limits.");
        directory = Path.GetFullPath(directory, contentRoot);
        Directory.CreateDirectory(directory);
        var loggerConfiguration = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", applicationName)
            .Enrich.WithProperty("ProcessId", Environment.ProcessId)
            .WriteTo.File(Path.Combine(directory, applicationName + "-.log"),
                rollingInterval: RollingInterval.Day, fileSizeLimitBytes: size,
                rollOnFileSizeLimit: true, retainedFileCountLimit: retained, shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{Application}] [{SourceContext}] {Message:lj} {Properties:j}{NewLine}{Exception}");
        var seq = configuration.GetSection("Logging:Seq");
        if (seq.GetValue<bool>("Enabled"))
        {
            var url = seq["ServerUrl"];
            if (!Uri.TryCreate(url, UriKind.Absolute, out var address) || address.Scheme is not ("http" or "https"))
                throw new InvalidOperationException("Logging:Seq:ServerUrl must be an absolute HTTP or HTTPS URL.");
            logging.AddSeq(serverUrl: url!, apiKey: seq["ApiKey"]);
        }
        var logger = loggerConfiguration.CreateLogger();
        // Keep the host's Logging:LogLevel filters and existing console/debug providers.
        logging.Services.AddSingleton<ILoggerProvider>(_ => new SerilogLoggerProvider(logger, dispose: true));
        return logging;
    }
}
