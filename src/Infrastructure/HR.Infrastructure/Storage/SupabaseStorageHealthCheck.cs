using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace HR.Infrastructure.Storage;

/// <summary>
/// Registered as the "storage" named health check (System Health Dashboard, Platform Monitoring
/// epic). Calls Supabase Storage's GET /storage/v1/bucket endpoint (list buckets) using the same
/// service-role key as SupabaseProfilePhotoStorageService — a cheap, real reachability probe (one
/// list call, no file transfer). Reports Degraded (not Unhealthy) when Supabase Storage isn't
/// configured (LocalProfilePhotoStorageService is in use instead), the same non-fatal-in-dev
/// convention as the other integration health checks.
/// </summary>
internal sealed class SupabaseStorageHealthCheck(
    IHttpClientFactory httpClientFactory,
    IOptions<SupabaseProfilePhotoStorageOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var supabaseUrl = options.Value.SupabaseUrl;
        var serviceRoleKey = options.Value.ServiceRoleKey;
        if (string.IsNullOrWhiteSpace(supabaseUrl) || string.IsNullOrWhiteSpace(serviceRoleKey))
        {
            return HealthCheckResult.Degraded("Supabase Storage is not configured.");
        }

        try
        {
            using var client = httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(
                HttpMethod.Get, new Uri(new Uri(supabaseUrl), "/storage/v1/bucket"));
            request.Headers.Add("apikey", serviceRoleKey);
            request.Headers.Add("Authorization", $"Bearer {serviceRoleKey}");

            using var response = await client.SendAsync(request, cancellationToken);

            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("Supabase Storage reachable.")
                : HealthCheckResult.Unhealthy($"Supabase Storage returned {(int)response.StatusCode}.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Supabase Storage could not be reached.", ex);
        }
    }
}
