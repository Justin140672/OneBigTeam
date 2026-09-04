using System.Security.Cryptography;
using HR.Infrastructure.Abstractions;
using HR.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Infrastructure.Tests;

/// <summary>
/// Ticket 9: <see cref="InfrastructureModule.ValidateSensitiveDataProtectionOrThrow"/> is the hard
/// production startup gate — it must throw <see cref="SensitiveDataProtectionException"/> (crashing
/// startup) whenever sensitive-data encryption is missing or misconfigured, and must not throw when
/// a valid key is configured and the self-test round-trips successfully.
/// </summary>
public class ValidateSensitiveDataProtectionOrThrowTests
{
    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static IServiceProvider BuildProvider(SensitiveDataProtectionOptions options)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISensitiveDataProtector>(_ => AesGcmSensitiveDataProtector.Create(options));
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Throws_when_no_keys_are_configured()
    {
        var services = BuildProvider(new SensitiveDataProtectionOptions { ActiveKeyId = "k1" });

        Assert.Throws<SensitiveDataProtectionException>(services.ValidateSensitiveDataProtectionOrThrow);
    }

    [Fact]
    public void Throws_when_active_key_id_does_not_match_any_configured_key()
    {
        var options = new SensitiveDataProtectionOptions { ActiveKeyId = "missing" };
        options.Keys["k1"] = NewKey();
        var services = BuildProvider(options);

        Assert.Throws<SensitiveDataProtectionException>(services.ValidateSensitiveDataProtectionOrThrow);
    }

    [Fact]
    public void Does_not_throw_when_a_valid_key_is_configured()
    {
        var options = new SensitiveDataProtectionOptions { ActiveKeyId = "k1" };
        options.Keys["k1"] = NewKey();
        var services = BuildProvider(options);

        var exception = Record.Exception(services.ValidateSensitiveDataProtectionOrThrow);

        Assert.Null(exception);
    }

    [Fact]
    public void Throws_when_services_argument_is_null()
    {
        IServiceProvider? services = null;

        Assert.Throws<ArgumentNullException>(() => services!.ValidateSensitiveDataProtectionOrThrow());
    }
}
