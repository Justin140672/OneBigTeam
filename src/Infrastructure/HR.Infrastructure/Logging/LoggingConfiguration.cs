using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;
using Serilog.Sinks.OpenTelemetry;

namespace HR.Infrastructure.Logging;

public static class LoggingConfiguration
{
    public static IHostBuilder UseSerilogWithDefaults(this IHostBuilder host) =>
        host.UseSerilog((context, services, cfg) =>
        {
            cfg
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.With(new SensitiveDataScrubbingEnricher())
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

    public static IApplicationBuilder UseLoggingMiddleware(this IApplicationBuilder app) =>
        app
            .UseMiddleware<CorrelationIdMiddleware>()
            .UseMiddleware<RequestLoggingMiddleware>();
}
