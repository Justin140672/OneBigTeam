using HR.Modules.Companies.Services;

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace HR.Modules.Companies.Tests.Services;

public class StripeHealthCheckTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CheckHealthAsync_Returns_Degraded_When_SecretKey_Not_Configured(string? secretKey)
    {
        var options = Options.Create(new StripeOptions { SecretKey = secretKey ?? "" });
        var healthCheck = new StripeHealthCheck(options);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Equal("Stripe is not configured.", result.Description);
    }

    // The Healthy and Unhealthy branches both require an outbound call to Stripe's real API (via
    // Stripe.net's BalanceService, which isn't injected/mockable here), so they aren't covered by
    // unit tests to avoid network-dependent, potentially flaky tests. The "not configured ->
    // Degraded" branch above needs no network call and is the fully-testable, most important case.
}
