using HR.Infrastructure.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace HR.Modules.DataImport.Services;

/// <summary>
/// Reliability review issue 2 (P1): readiness probe proving the configured Supabase Storage project
/// backing import files is actually reachable — mirrors DocumentStorageHealthCheck. Only registered
/// when Supabase import file storage is configured (see DataImportModule.AddImportFileStorage);
/// Development/test environments using LocalImportFileStorageService have nothing to probe. Tagged
/// "degraded" (not "critical"): data import is impaired, not the whole platform, if this dependency
/// is unavailable.
/// </summary>
internal sealed class SupabaseImportFileStorageHealthCheck(
    IHttpClientFactory httpClientFactory,
    IOptions<SupabaseImportFileStorageOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var supabaseUrl = options.Value.SupabaseUrl;
        var serviceRoleKey = options.Value.ServiceRoleKey;
        var bucketName = options.Value.BucketName;

        if (string.IsNullOrWhiteSpace(supabaseUrl) || string.IsNullOrWhiteSpace(serviceRoleKey) || string.IsNullOrWhiteSpace(bucketName))
        {
            return HealthCheckResult.Unhealthy("Import file storage (Supabase) is not configured.");
        }

        try
        {
            using var client = httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(
                HttpMethod.Get, new Uri(new Uri(supabaseUrl), "/storage/v1/bucket"));
            request.Headers.Add("apikey", serviceRoleKey);
            request.Headers.Add("Authorization", $"Bearer {serviceRoleKey}");

            using var response = await client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return HealthCheckResult.Unhealthy($"Import file storage (Supabase) returned {(int)response.StatusCode}.");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            return SupabaseStorageBucketCheck.ContainsBucket(body, bucketName)
                ? HealthCheckResult.Healthy("Import file storage (Supabase) reachable.")
                : HealthCheckResult.Unhealthy("Import file storage (Supabase) is reachable but the configured bucket was not found.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Import file storage (Supabase) could not be reached.", ex);
        }
    }
}
