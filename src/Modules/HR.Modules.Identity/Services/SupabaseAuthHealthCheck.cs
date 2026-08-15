using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace HR.Modules.Identity.Services;

/// <summary>
/// Registered as the "auth" named health check (System Health Dashboard, Platform Monitoring
/// epic). Calls Supabase Auth's public, unauthenticated GET /auth/v1/settings endpoint — a cheap,
/// real reachability probe that requires no admin credentials and never touches user data.
/// UNVERIFIED against a live Supabase project (same caveat as SupabaseAuthGateway) — if this
/// endpoint doesn't behave as documented, this check should be revisited. Reports Degraded (not
/// Unhealthy) when Supabase Auth isn't configured, the same non-fatal-in-dev convention as
/// StripeHealthCheck/PostmarkHealthCheck.
/// </summary>
internal sealed class SupabaseAuthHealthCheck(IHttpClientFactory httpClientFactory, IOptions<SupabaseAuthOptions> options)
    : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var projectUrl = options.Value.ProjectUrl;
        if (string.IsNullOrWhiteSpace(projectUrl))
        {
            return HealthCheckResult.Degraded("Supabase Auth is not configured.");
        }

        try
        {
            using var client = httpClientFactory.CreateClient();
            using var response = await client.GetAsync(
                new Uri(new Uri(projectUrl), "/auth/v1/settings"), cancellationToken);

            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("Supabase Auth reachable.")
                : HealthCheckResult.Unhealthy($"Supabase Auth returned {(int)response.StatusCode}.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Supabase Auth could not be reached.", ex);
        }
    }
}
