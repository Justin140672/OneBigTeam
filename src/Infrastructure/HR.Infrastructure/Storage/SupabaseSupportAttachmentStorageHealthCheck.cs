using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace HR.Infrastructure.Storage;

/// <summary>
/// Security review ticket 3 (P1): readiness probe for the Supabase Storage project backing support
/// attachments, mirroring <see cref="SupabaseStorageHealthCheck"/>. Only registered when Supabase
/// support-attachment storage is configured (see
/// InfrastructureModule.AddSupportAttachmentStorageService) — Development/test using
/// LocalSupportAttachmentStorageService has nothing to probe.
/// </summary>
internal sealed class SupabaseSupportAttachmentStorageHealthCheck(
    IHttpClientFactory httpClientFactory,
    IOptions<SupabaseSupportAttachmentStorageOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var supabaseUrl = options.Value.SupabaseUrl;
        var serviceRoleKey = options.Value.ServiceRoleKey;

        if (string.IsNullOrWhiteSpace(supabaseUrl) || string.IsNullOrWhiteSpace(serviceRoleKey))
        {
            return HealthCheckResult.Unhealthy("Support attachment storage (Supabase) is not configured.");
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
                ? HealthCheckResult.Healthy("Support attachment storage (Supabase) reachable.")
                : HealthCheckResult.Unhealthy($"Support attachment storage (Supabase) returned {(int)response.StatusCode}.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Support attachment storage (Supabase) could not be reached.", ex);
        }
    }
}
