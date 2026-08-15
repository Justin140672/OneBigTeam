using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace HR.Infrastructure.Email;

/// <summary>
/// Registered as the "email" named health check (System Health Dashboard, Platform Monitoring
/// epic). Calls Postmark's read-only "server info" endpoint (GET /server) using the same
/// X-Postmark-Server-Token auth as PostmarkEmailSender — a cheap, real reachability check that
/// never actually sends an email. Reports Degraded (not Unhealthy) when Postmark isn't configured
/// (LoggingEmailSender is in use instead), the same non-fatal-in-dev convention as StripeHealthCheck.
/// </summary>
internal sealed class PostmarkHealthCheck(IHttpClientFactory httpClientFactory, IOptions<PostmarkOptions> options)
    : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var serverToken = options.Value.ServerToken;
        if (string.IsNullOrWhiteSpace(serverToken))
        {
            return HealthCheckResult.Degraded("Postmark is not configured; falling back to logging email sender.");
        }

        try
        {
            using var client = httpClientFactory.CreateClient();
            client.BaseAddress = new Uri("https://api.postmarkapp.com/");
            client.DefaultRequestHeaders.Add("X-Postmark-Server-Token", serverToken);
            client.DefaultRequestHeaders.Add("Accept", "application/json");

            using var response = await client.GetAsync("server", cancellationToken);

            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("Postmark API reachable.")
                : HealthCheckResult.Unhealthy($"Postmark API returned {(int)response.StatusCode}.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Postmark API could not be reached.", ex);
        }
    }
}
