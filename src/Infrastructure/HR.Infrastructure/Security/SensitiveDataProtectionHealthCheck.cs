using HR.Infrastructure.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HR.Infrastructure.Security;

/// <summary>
/// Ticket 9 — operational safety for sensitive-data encryption.
///
/// Confirms that application-level encryption is actually configured and usable, without performing
/// or exposing any sensitive operation: it encrypts and decrypts a fixed, non-sensitive sentinel
/// string with the active key. It never touches real customer data and never emits key material,
/// key ids, ciphertext or exception detail into the health payload — only a curated description.
///
/// Registered with the <c>ready</c> tag so it appears in readiness detail and contributes a
/// <c>Degraded</c> overall status when broken, but is intentionally NOT tagged <c>critical</c>:
/// the hard production gate is the fail-fast startup check
/// (<see cref="InfrastructureModule.ValidateSensitiveDataProtectionOrThrow"/>), which prevents a
/// misconfigured instance from ever serving traffic in the first place.
/// </summary>
internal sealed class SensitiveDataProtectionHealthCheck(IServiceProvider services) : IHealthCheck
{
    internal const string SelfTestValue = "obt-sensitive-data-encryption-selftest";

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var protector = services.GetRequiredService<ISensitiveDataProtector>();
            var token = protector.Protect(SelfTestValue);
            var roundTripped = protector.Unprotect(token);

            return Task.FromResult(roundTripped == SelfTestValue
                ? HealthCheckResult.Healthy("Sensitive-data encryption is configured and the active key is usable.")
                : HealthCheckResult.Unhealthy("Sensitive-data encryption self-test failed: round-trip mismatch."));
        }
        catch (SensitiveDataProtectionException)
        {
            // Message is deliberately generic and safe to log, but we still do not surface it here.
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Sensitive-data encryption is not configured or the active key is not usable."));
        }
        catch (Exception)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Sensitive-data encryption could not be verified."));
        }
    }
}
