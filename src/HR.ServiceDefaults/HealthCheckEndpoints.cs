using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// NFR-03: production-safe liveness and readiness endpoints, shared by every service that calls
/// <c>AddServiceDefaults()</c>/<c>MapDefaultEndpoints()</c> (HR.Api, HR.Web, HR.Marketing,
/// HR.Admin.Web).
///
/// <para>
/// Design:
/// <list type="bullet">
/// <item><c>/alive</c> — liveness. Anonymous, always mapped (all environments). Evaluates ONLY
/// checks tagged <c>live</c> (the built-in "self" check) — never touches a database, HTTP
/// dependency, or credential. Answers "is this process responsive?" and nothing else.</item>
/// <item><c>/health/ready</c> — readiness. Anonymous, always mapped. Evaluates every dependency
/// check. Returns <c>503</c> only when a check tagged <c>critical</c> is Unhealthy; a failing
/// non-critical ("degraded"-tagged) dependency yields <c>200</c> with an overall status of
/// <c>Degraded</c> so the platform keeps serving traffic. The public body is minimal
/// (<c>{"status":"..."}</c>) and discloses no per-check names, descriptions, exceptions, or
/// infrastructure detail. Full per-check detail is returned only when the caller presents the
/// configured <c>HealthChecks:ReadinessDetailToken</c> via the <c>X-Health-Token</c> header, or in
/// the Development environment.</item>
/// <item><c>/health</c> — the original Aspire aggregate endpoint. Still Development-only.</item>
/// </list>
/// </para>
/// </summary>
public static class HealthCheckEndpoints
{
    public const string LivenessPath = "/alive";
    public const string ReadinessPath = "/health/ready";
    public const string DetailTokenHeader = "X-Health-Token";
    public const string DetailTokenConfigKey = "HealthChecks:ReadinessDetailToken";

    public const string LiveTag = "live";
    public const string ReadyTag = "ready";
    public const string CriticalTag = "critical";
    public const string DegradedTag = "degraded";

    public static void MapLivenessAndReadiness(WebApplication app)
    {
        // Liveness: process responsiveness only. No dependency probing.
        app.MapHealthChecks(LivenessPath, new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(LiveTag),
            ResponseWriter = static (context, _) =>
            {
                context.Response.ContentType = "application/json";
                return context.Response.WriteAsync("""{"status":"Healthy"}""");
            },
        }).AllowAnonymous();

        // Readiness: evaluate all dependency checks, but only a failing *critical* dependency
        // makes the service "not ready".
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

        var includeDetail = ShouldIncludeDetail(context);

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

    private static bool ShouldIncludeDetail(HttpContext context)
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
        return CryptographicOperations.FixedTimeEquals(presentedBytes, configuredBytes);
    }
}
