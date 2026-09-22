using HR.Infrastructure.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace HR.Modules.Documents.Services;

/// <summary>
/// Security review ticket 3 (P1): readiness probe proving the configured Supabase Storage project
/// backing document uploads is actually reachable — mirrors
/// HR.Infrastructure.Storage.SupabaseStorageHealthCheck's cheap "list buckets" probe. Only
/// registered when Supabase document storage is configured (see
/// DocumentsModule.AddStorageService); Development/test environments using
/// LocalDocumentStorageService have nothing to probe.
/// </summary>
internal sealed class DocumentStorageHealthCheck(
    IHttpClientFactory httpClientFactory,
    IOptions<SupabaseStorageOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var supabaseUrl = options.Value.SupabaseUrl;
        var serviceRoleKey = options.Value.ServiceRoleKey;
        var bucketName = options.Value.BucketName;

        if (string.IsNullOrWhiteSpace(supabaseUrl) || string.IsNullOrWhiteSpace(serviceRoleKey) || string.IsNullOrWhiteSpace(bucketName))
        {
            return HealthCheckResult.Unhealthy("Document storage (Supabase) is not configured.");
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
                return HealthCheckResult.Unhealthy($"Document storage (Supabase) returned {(int)response.StatusCode}.");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            return SupabaseStorageBucketCheck.ContainsBucket(body, bucketName)
                ? HealthCheckResult.Healthy("Document storage (Supabase) reachable.")
                : HealthCheckResult.Unhealthy("Document storage (Supabase) is reachable but the configured bucket was not found.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Document storage (Supabase) could not be reached.", ex);
        }
    }
}
