using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Turn on resilience by default. The stock defaults (10s per attempt, 30s total) are
            // tuned for a healthy cloud host. Our E2E nightly job runs every service + Postgres +
            // headless Chromium on a 2-vCPU runner, where a single internal web->api hop (e.g.
            // HR.Web's "hrapi" client calling POST /api/login) can legitimately take far longer
            // than 10s under CPU starvation. When the attempt timeout fires there, HR.Web's
            // catch-all turns it into a bogus "Something went wrong. Please try again." Widen the
            // per-attempt and total budgets and add one more retry so backend slowness surfaces as
            // a slow request that eventually succeeds, not a spurious error. All traffic governed by
            // this handler is internal service-to-service, so the larger budget is safe.
            // Ticket 3 (P1) follow-up: must be added BEFORE AddStandardResilienceHandler so it wraps
            // it and can attach the request method to the shared ResilienceContext ahead of every
            // attempt - see RequestMethodCapturingHandler for why.
            http.AddHttpMessageHandler(() => new RequestMethodCapturingHandler());

            http.AddStandardResilienceHandler(options =>
            {
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(30);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(120);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
                options.Retry.MaxRetryAttempts = 4;
                options.Retry.Delay = TimeSpan.FromMilliseconds(500);
                options.Retry.UseJitter = true;

                // Ticket 3 (P1): the standard predicate retries any transient failure regardless of
                // HTTP method. Several handlers commit their write before publishing audit events, so
                // a retried POST/PUT/PATCH/DELETE after a failed-looking-but-committed response (or a
                // dropped response) can repeat the mutation (e.g. double leave adjustment, duplicate
                // auto-numbered asset). Only auto-retry methods that are safe to repeat.
                //
                // On a timeout or connection failure Outcome.Result is null - there's no response to
                // read RequestMessage.Method off. Fall back to the method RequestMethodCapturingHandler
                // attached to this operation's ResilienceContext before the first attempt, so a safe
                // GET/HEAD/OPTIONS still retries after a dropped connection or attempt timeout instead
                // of being (incorrectly) treated as unsafe just because there was no response object.
                options.Retry.ShouldHandle = args =>
                {
                    var method = args.Outcome.Result?.RequestMessage?.Method
                        ?? (args.Context.Properties.TryGetValue(RequestMethodCapturingHandler.RequestMethodKey, out var captured)
                            ? captured
                            : null);
                    var isSafeToRetry = method == HttpMethod.Get || method == HttpMethod.Head || method == HttpMethod.Options;

                    return ValueTask.FromResult(isSafeToRetry && HttpClientResiliencePredicates.IsTransient(args.Outcome));
                };
            });

            http.AddServiceDiscovery();
        });


        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing.AddProcessor(new SensitiveDataRedactingProcessor());

                tracing.AddSource(builder.Environment.ApplicationName)
                    .AddAspNetCoreInstrumentation(tracing =>
                        tracing.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                            && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath)
                    )
                    .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }


        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        HealthCheckEndpoints.MapLivenessAndReadiness(app);

        // Ticket 5: anonymous running-release identity probe. Used by the deploy pipeline to verify
        // the exact requested release is live on every required service (an old instance reporting
        // the old SHA must fail verification). Discloses only service/sha/version/environment/start.
        app.MapGet("/health/release", (IHostEnvironment env) =>
                Results.Json(ReleaseIdentity.Payload(env.EnvironmentName, env.ApplicationName)))
            .AllowAnonymous()
            .WithName("HealthRelease");

        if (app.Environment.IsDevelopment())
        {
            app.MapHealthChecks(HealthEndpointPath);
        }

        return app;
    }
}
