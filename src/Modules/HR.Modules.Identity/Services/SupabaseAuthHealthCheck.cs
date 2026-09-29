using System.Net;
using System.Net.Http.Headers;

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace HR.Modules.Identity.Services;

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
