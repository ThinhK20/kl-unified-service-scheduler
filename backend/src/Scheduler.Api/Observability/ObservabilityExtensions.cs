using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scheduler.Application.Abstractions;

namespace Scheduler.Api.Observability;

/// <summary>
/// Logs, traces and metrics setup (SPEC section 10). Everything is driven by configuration:
/// <list type="bullet">
/// <item><c>Logging:Format</c>: "Json" (one JSON object per line, with scopes) or "Simple" (one readable line).
/// Default: Simple in Development, Json everywhere else.</item>
/// <item><c>Observability:Otlp:Endpoint</c>: OTLP collector address, for example "http://localhost:4317". Empty = no OTLP.
/// When set, traces, metrics and logs are sent there.</item>
/// <item><c>Observability:ConsoleExporter</c>: true writes traces and metrics to the console (for local checks). Default false.</item>
/// <item><c>Observability:ServiceName</c>: service name on every span and metric. Default "scheduler-api".</item>
/// </list>
/// </summary>
public static class ObservabilityExtensions
{
    public const string LogFormatKey = "Logging:Format";
    public const string OtlpEndpointKey = "Observability:Otlp:Endpoint";
    public const string ConsoleExporterKey = "Observability:ConsoleExporter";
    public const string ServiceNameKey = "Observability:ServiceName";
    public const string DefaultServiceName = "scheduler-api";

    /// <summary>Console logging: JSON or simple text, both with scopes (so each line has the correlation id).</summary>
    public static ILoggingBuilder AddSchedulerConsoleLogging(this ILoggingBuilder logging, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(logging);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var format = configuration[LogFormatKey];
        var useJson = string.IsNullOrWhiteSpace(format)
            ? !environment.IsDevelopment()
            : format.Trim().ToUpperInvariant() switch
            {
                "JSON" => true,
                "SIMPLE" => false,
                _ => throw new InvalidOperationException($"{LogFormatKey} must be \"Json\" or \"Simple\", not \"{format}\"."),
            };

        logging.ClearProviders();
        if (useJson)
        {
            logging.AddJsonConsole(options =>
            {
                options.IncludeScopes = true;
                options.UseUtcTimestamp = true;
                options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
            });
        }
        else
        {
            logging.AddSimpleConsole(options =>
            {
                options.IncludeScopes = true;
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss ";
            });
        }

        return logging;
    }

    /// <summary>Our instruments, the appointment telemetry, and OpenTelemetry tracing and metrics with the configured exporters.</summary>
    public static IServiceCollection AddSchedulerObservability(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddMetrics();
        services.AddSingleton<SchedulerInstruments>();
        services.AddSingleton<IAppointmentTelemetry, AppointmentTelemetry>();

        var otlpEndpoint = ReadOtlpEndpoint(configuration);
        var console = configuration.GetValue(ConsoleExporterKey, false);
        var serviceName = configuration[ServiceNameKey] is { Length: > 0 } name ? name : DefaultServiceName;

        var openTelemetry = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(SchedulerInstruments.Name)
                    // Health probes run every few seconds; their spans are only noise.
                    .AddAspNetCoreInstrumentation(options => options.Filter = context => !context.Request.Path.StartsWithSegments("/health"))
                    .AddSqlClientInstrumentation();
                if (console)
                {
                    tracing.AddConsoleExporter();
                }

                if (otlpEndpoint is not null)
                {
                    tracing.AddOtlpExporter(options => options.Endpoint = otlpEndpoint);
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter(SchedulerInstruments.Name)
                    .AddAspNetCoreInstrumentation(); // the built-in ASP.NET Core and Kestrel meters
                if (console)
                {
                    metrics.AddConsoleExporter();
                }

                if (otlpEndpoint is not null)
                {
                    metrics.AddOtlpExporter(options => options.Endpoint = otlpEndpoint);
                }
            });

        // Logs also go to OTLP when a collector is set, so a dashboard shows them next to the traces.
        if (otlpEndpoint is not null)
        {
            openTelemetry.WithLogging(
                logging => logging.AddOtlpExporter(options => options.Endpoint = otlpEndpoint),
                options =>
                {
                    options.IncludeScopes = true;
                    options.IncludeFormattedMessage = true;
                });
        }

        return services;
    }

    private static Uri? ReadOtlpEndpoint(IConfiguration configuration)
    {
        var value = configuration[OtlpEndpointKey];
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            ? uri
            : throw new InvalidOperationException($"{OtlpEndpointKey} must be an absolute URL, for example http://localhost:4317.");
    }
}
