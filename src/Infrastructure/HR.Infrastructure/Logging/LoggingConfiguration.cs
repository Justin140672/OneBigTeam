using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;
using Serilog.Sinks.OpenTelemetry;

namespace HR.Infrastructure.Logging;

public static class LoggingConfiguration
{
    /// <summary>
    /// Replaces the default Microsoft logging providers with Serilog.
    /// Reads minimum-level overrides from the "Serilog" appsettings section.
    /// Writes to Console (human-readable in dev, JSON in production).
    /// Forwards to OTLP when OTEL_EXPORTER_OTLP_ENDPOINT is configured.
    /// </summary>
    public static IHostBuilder UseSerilogWithDefaults(this IHostBuilder host) =>
        host.UseSerilog((context, services, cfg) =>
        {
            cfg
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithEnvironmentName()
                .Enrich.WithMachineName()
                .Enrich.WithProperty("Application", context.HostingEnvironment.ApplicationName);

            if (context.HostingEnvironment.IsDevelopment())
            {
                cfg.WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {CorrelationId} {Message:lj}{NewLine}{Exception}",
                    restrictedToMinimumLevel: LogEventLevel.Debug);
            }
            else
            {
                cfg.WriteTo.Console(new JsonFormatter());
            }

            var otlpEndpoint = context.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
            if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            {
                // Forward logs to the same OTLP collector as metrics and traces.
                // HTTP/Protobuf requires the /v1/logs path; the existing OTel SDK
                // exporter handles this path automatically, so we mirror it here.
                cfg.WriteTo.OpenTelemetry(opt =>
                {
                    opt.Endpoint = $"{otlpEndpoint.TrimEnd('/')}/v1/logs";
                    opt.Protocol = OtlpProtocol.HttpProtobuf;
                    opt.ResourceAttributes = new Dictionary<string, object>
                    {
                        ["service.name"] = context.HostingEnvironment.ApplicationName,
                        ["deployment.environment"] = context.HostingEnvironment.EnvironmentName,
                    };
                });
            }
        });

    /// <summary>
    /// Registers the correlation-ID and request-logging middleware.
    /// Call this before UseAuthentication so every request gets a correlation ID,
    /// even failed auth requests.
    /// </summary>
    public static IApplicationBuilder UseLoggingMiddleware(this IApplicationBuilder app) =>
        app
            .UseMiddleware<CorrelationIdMiddleware>()
            .UseMiddleware<RequestLoggingMiddleware>();
}
