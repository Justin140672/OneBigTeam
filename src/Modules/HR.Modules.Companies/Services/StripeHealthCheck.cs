using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

using Stripe;

namespace HR.Modules.Companies.Services;

internal sealed class StripeHealthCheck(IOptions<StripeOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var secretKey = options.Value.SecretKey;
        if (string.IsNullOrWhiteSpace(secretKey))
        {
            return HealthCheckResult.Degraded("Stripe is not configured.");
        }

        try
        {
            var requestOptions = new RequestOptions { ApiKey = secretKey };
            var service = new BalanceService();
            await service.GetAsync(requestOptions, cancellationToken);

            return HealthCheckResult.Healthy("Stripe API reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Stripe API could not be reached.", ex);
        }
    }
}
