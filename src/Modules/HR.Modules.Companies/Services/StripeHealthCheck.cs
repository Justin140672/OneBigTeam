using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

using Stripe;

namespace HR.Modules.Companies.Services;

/// <summary>
/// Registered as the "stripe" named health check (System Health Dashboard, Platform Monitoring
/// epic). A cheap, real reachability probe — retrieves the Stripe account balance, which is a
/// lightweight read-only call that doesn't touch invoice/customer data — rather than a fake ping.
/// Reports Degraded (not Unhealthy) when no secret key is configured, matching the existing
/// "StripeConfigured" convention used elsewhere in this module (Stripe absence is an expected,
/// non-fatal state in dev/test environments, not a platform outage).
/// </summary>
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
