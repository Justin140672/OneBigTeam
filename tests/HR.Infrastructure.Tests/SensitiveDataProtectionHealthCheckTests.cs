using System.Security.Cryptography;
using HR.Infrastructure.Abstractions;
using HR.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HR.Infrastructure.Tests;

/// <summary>
/// Ticket 9: <see cref="SensitiveDataProtectionHealthCheck"/> proves the active encryption key is
/// usable via a fixed, non-sensitive self-test round-trip, and never leaks key material, key ids,
/// ciphertext or exception detail through its <see cref="HealthCheckResult.Description"/>.
/// </summary>
public class SensitiveDataProtectionHealthCheckTests
{
    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static IServiceProvider BuildProviderWithValidProtector()
    {
        var options = new SensitiveDataProtectionOptions { ActiveKeyId = "k1" };
        options.Keys["k1"] = NewKey();
        var protector = AesGcmSensitiveDataProtector.Create(options);

        var services = new ServiceCollection();
        services.AddSingleton<ISensitiveDataProtector>(protector);
        return services.BuildServiceProvider();
    }

    private static IServiceProvider BuildProviderWithMisconfiguredProtector()
    {
        // Mirrors InfrastructureModule.AddSensitiveDataProtection's lazy factory registration: the
        // factory itself throws SensitiveDataProtectionException when resolved, because no keys are
        // configured.
        var services = new ServiceCollection();
        services.AddSingleton<ISensitiveDataProtector>(_ =>
            AesGcmSensitiveDataProtector.Create(new SensitiveDataProtectionOptions()));
        return services.BuildServiceProvider();
    }

    private static IServiceProvider BuildProviderWhereResolutionThrowsUnexpectedException()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISensitiveDataProtector>(_ =>
            throw new InvalidOperationException("boom"));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Returns_Healthy_when_protector_resolves_and_round_trips()
    {
        var check = new SensitiveDataProtectionHealthCheck(BuildProviderWithValidProtector());

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(
            "Sensitive-data encryption is configured and the active key is usable.",
            result.Description);
    }

    [Fact]
    public async Task Returns_Unhealthy_when_protector_resolution_throws_SensitiveDataProtectionException()
    {
        var check = new SensitiveDataProtectionHealthCheck(BuildProviderWithMisconfiguredProtector());

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal(
            "Sensitive-data encryption is not configured or the active key is not usable.",
            result.Description);
    }

    [Fact]
    public async Task Returns_Unhealthy_when_protector_resolution_throws_an_unexpected_exception()
    {
        var check = new SensitiveDataProtectionHealthCheck(BuildProviderWhereResolutionThrowsUnexpectedException());

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("Sensitive-data encryption could not be verified.", result.Description);
    }

    [Fact]
    public async Task Returns_Unhealthy_when_round_trip_mismatches()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISensitiveDataProtector>(new RoundTripMismatchProtector());
        var check = new SensitiveDataProtectionHealthCheck(services.BuildServiceProvider());

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal(
            "Sensitive-data encryption self-test failed: round-trip mismatch.",
            result.Description);
    }

    [Fact]
    public async Task Descriptions_never_contain_the_self_test_value_or_a_ciphertext_token()
    {
        var validCheck = new SensitiveDataProtectionHealthCheck(BuildProviderWithValidProtector());
        var healthyResult = await validCheck.CheckHealthAsync(new HealthCheckContext());

        var misconfiguredCheck = new SensitiveDataProtectionHealthCheck(BuildProviderWithMisconfiguredProtector());
        var unhealthyResult = await misconfiguredCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.DoesNotContain(SensitiveDataProtectionHealthCheck.SelfTestValue, healthyResult.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("OBTENC1", healthyResult.Description, StringComparison.Ordinal);
        Assert.DoesNotContain(SensitiveDataProtectionHealthCheck.SelfTestValue, unhealthyResult.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("OBTENC1", unhealthyResult.Description, StringComparison.Ordinal);
    }

    /// <summary>A fake protector whose Unprotect deliberately does not return the original value.</summary>
    private sealed class RoundTripMismatchProtector : ISensitiveDataProtector
    {
        public string Protect(string plaintext) => "token";

        public string Unprotect(string protectedValue) => "not-the-self-test-value";

        public bool TryUnprotect(string? value, out string? plaintext)
        {
            plaintext = "not-the-self-test-value";
            return true;
        }

        public bool IsProtected(string? value) => true;
    }
}
