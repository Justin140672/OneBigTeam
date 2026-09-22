using HR.Infrastructure.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace HR.Modules.Recruitment.Services;

/// <summary>
/// Reliability review issue 2 (P1): readiness probe proving the configured Supabase Storage project
/// backing candidate documents is actually reachable — mirrors DocumentStorageHealthCheck /
/// SupabaseSupportAttachmentStorageHealthCheck. Only registered when Supabase candidate document
/// storage is configured (see RecruitmentModule.AddCandidateDocumentStorage); Development/test
/// environments using LocalCandidateDocumentStorageService have nothing to probe. Tagged "degraded"
/// (not "critical"): candidate document upload/download is impaired, not the whole platform, if this
/// dependency is unavailable.
/// </summary>
internal sealed class SupabaseCandidateDocumentStorageHealthCheck(
    IHttpClientFactory httpClientFactory,
    IOptions<SupabaseCandidateDocumentStorageOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var supabaseUrl = options.Value.SupabaseUrl;
        var serviceRoleKey = options.Value.ServiceRoleKey;
        var bucketName = options.Value.BucketName;

        if (string.IsNullOrWhiteSpace(supabaseUrl) || string.IsNullOrWhiteSpace(serviceRoleKey) || string.IsNullOrWhiteSpace(bucketName))
        {
            return HealthCheckResult.Unhealthy("Candidate document storage (Supabase) is not configured.");
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
                return HealthCheckResult.Unhealthy($"Candidate document storage (Supabase) returned {(int)response.StatusCode}.");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            return SupabaseStorageBucketCheck.ContainsBucket(body, bucketName)
                ? HealthCheckResult.Healthy("Candidate document storage (Supabase) reachable.")
                : HealthCheckResult.Unhealthy("Candidate document storage (Supabase) is reachable but the configured bucket was not found.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Candidate document storage (Supabase) could not be reached.", ex);
        }
    }
}
