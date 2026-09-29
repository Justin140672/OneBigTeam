using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace HR.Infrastructure.Email;

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
