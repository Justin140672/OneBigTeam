using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.Hosting;

public static class HealthCheckEndpoints
{
    public const string LivenessPath = "/alive";
    public const string ReadinessPath = "/health/ready";
    public const string DetailTokenHeader = "X-Health-Token";
    public const string DetailTokenConfigKey = "HealthChecks:ReadinessDetailToken";

    public const string DetailTokenPreviousConfigKey = "HealthChecks:ReadinessDetailTokenPrevious";

    public const string LiveTag = "live";
    public const string ReadyTag = "ready";
    public const string CriticalTag = "critical";
    public const string DegradedTag = "degraded";

    public static void MapLivenessAndReadiness(WebApplication app)
    {
        app.MapHealthChecks(LivenessPath, new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(LiveTag),
            ResponseWriter = static (context, _) =>
            {
                context.Response.ContentType = "application/json";
                return context.Response.WriteAsync("""{"status":"Healthy"}""");
            },
        }).AllowAnonymous();

        app.MapHealthChecks(ReadinessPath, new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            Predicate = registration => !registration.Tags.Contains(LiveTag),
            ResponseWriter = WriteReadinessResponse,
        }).AllowAnonymous();
    }

    private static Task WriteReadinessResponse(HttpContext context, HealthReport report)
    {
        var criticalUnhealthy = report.Entries.Any(entry =>
            entry.Value.Tags.Contains(CriticalTag) && entry.Value.Status == HealthStatus.Unhealthy);

        var anyProblem = report.Entries.Any(entry => entry.Value.Status != HealthStatus.Healthy);

        var overall = criticalUnhealthy ? "Unhealthy" : anyProblem ? "Degraded" : "Healthy";

        context.Response.StatusCode = criticalUnhealthy
            ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status200OK;
        context.Response.ContentType = "application/json";

        var includeDetail = HasDetailAccess(context);

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("status", overall);

            if (includeDetail)
            {
                writer.WriteStartArray("checks");
                foreach (var entry in report.Entries)
                {
                    writer.WriteStartObject();
                    writer.WriteString("name", entry.Key);
                    writer.WriteString("status", entry.Value.Status.ToString());
                    writer.WriteBoolean("critical", entry.Value.Tags.Contains(CriticalTag));
                    // entry.Description is a curated, non-sensitive string owned by each health
                    // check. entry.Exception / entry.Data are deliberately never serialised — they
                    // can carry connection strings, hosts, and internal stack detail.
                    if (!string.IsNullOrWhiteSpace(entry.Value.Description))
                    {
                        writer.WriteString("description", entry.Value.Description);
                    }

                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        return context.Response.Body.WriteAsync(buffer.ToArray()).AsTask();
    }

    /// <summary>
    /// Security review ticket 6 (P2): the same detail-gating check used by <c>/health/ready</c>,
    /// exposed publicly so other detailed operational health endpoints (<c>/health/background-jobs</c>,
    /// <c>/health/startup-migrations</c>) can require the identical token before disclosing
    /// per-check/per-module names, descriptions, or exception detail. Works purely off
    /// <see cref="HttpContext.RequestServices"/> and <see cref="HttpContext.Request"/>, so it also
    /// works in the reduced pipeline HR.Api falls back to when a required startup migration fails
    /// (no authentication/authorization middleware is installed on that path).
    /// </summary>
    public static bool HasDetailAccess(HttpContext context)
    {
        var environment = context.RequestServices.GetService(typeof(IHostEnvironment)) as IHostEnvironment;
        if (environment is not null && environment.IsDevelopment())
        {
            return true;
        }

        var configuration = context.RequestServices.GetService(typeof(IConfiguration)) as IConfiguration;
        var configuredToken = configuration?[DetailTokenConfigKey];
        if (string.IsNullOrWhiteSpace(configuredToken))
        {
            return false;
        }

        if (!context.Request.Headers.TryGetValue(DetailTokenHeader, out var presented)
            || string.IsNullOrEmpty(presented))
        {
            return false;
        }

        var presentedBytes = Encoding.UTF8.GetBytes(presented.ToString());
        var configuredBytes = Encoding.UTF8.GetBytes(configuredToken);
        if (CryptographicOperations.FixedTimeEquals(presentedBytes, configuredBytes))
        {
            return true;
        }

        var previousToken = configuration?[DetailTokenPreviousConfigKey];
        if (string.IsNullOrWhiteSpace(previousToken))
        {
            return false;
        }

        var previousBytes = Encoding.UTF8.GetBytes(previousToken);
        return CryptographicOperations.FixedTimeEquals(presentedBytes, previousBytes);
    }
}
