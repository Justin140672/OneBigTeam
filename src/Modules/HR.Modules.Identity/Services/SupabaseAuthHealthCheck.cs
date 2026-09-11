using System.Net;
using System.Net.Http.Headers;

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace HR.Modules.Identity.Services;

/// <summary>
/// Registered as the "auth" named health check (System Health Dashboard, Platform Monitoring
/// epic). Calls Supabase Auth's GET /auth/v1/settings endpoint — a cheap reachability probe that
/// never touches user data.
/// <para>
/// Verified against a live Supabase project (2026-09-10): /auth/v1/settings returns 401 without
/// an <c>apikey</c> header and 200 when the anon/publishable key is supplied as both the
/// <c>apikey</c> header and a bearer token. This probe therefore sends the publishable key and
/// treats any authentication-level response (401/403) as "reachable" → Healthy, since it only
/// claims to test that Supabase Auth is up, not that our credentials are valid. Other non-success
/// codes (5xx etc.) are Unhealthy.
/// </para>
/// <para>
/// Reports Degraded (not Unhealthy) when Supabase Auth isn't configured (no ProjectUrl), the same
/// non-fatal-in-dev convention as StripeHealthCheck/PostmarkHealthCheck. A missing PublishableKey
/// while ProjectUrl is set is also treated as Degraded ("not fully configured") rather than
/// forcing an always-401 call.
/// </para>
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

        var publishableKey = options.Value.PublishableKey;
        if (string.IsNullOrWhiteSpace(publishableKey))
        {
            return HealthCheckResult.Degraded("Supabase Auth is not fully configured (no publishable key).");
        }

        try
        {
            using var client = httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(
                HttpMethod.Get, new Uri(new Uri(projectUrl), "/auth/v1/settings"));
            request.Headers.TryAddWithoutValidation("apikey", publishableKey);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", publishableKey);

            using var response = await client.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return HealthCheckResult.Healthy("Supabase Auth reachable.");
            }

            // An authentication-level response still proves Supabase Auth is up and answering.
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return HealthCheckResult.Healthy(
                    $"Supabase Auth reachable (returned {(int)response.StatusCode}).");
            }

            return HealthCheckResult.Unhealthy($"Supabase Auth returned {(int)response.StatusCode}.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Supabase Auth could not be reached.", ex);
        }
    }
}
