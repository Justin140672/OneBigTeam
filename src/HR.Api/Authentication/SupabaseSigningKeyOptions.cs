using Microsoft.Extensions.Configuration;

namespace HR.Api.Authentication;

/// <summary>
/// Strongly-typed configuration for Supabase JWT signing-key refresh resilience (Ticket 6).
/// Bound from the <c>SupabaseAuth:SigningKeyRefresh</c> configuration section; every value has a
/// safe production default so the section is optional.
/// </summary>
/// <remarks>
/// Each property is mapped to the ticket's named knob and to the concrete
/// <see cref="Microsoft.IdentityModel.Protocols.OpenIdConnect.ConfigurationManager{T}"/> setting it
/// drives. The library clamps some of these to internal minimums (noted below); the defaults here sit
/// at or above those minimums.
/// </remarks>
public sealed class SupabaseSigningKeyOptions
{
    public const string SectionName = "SupabaseAuth:SigningKeyRefresh";

    public TimeSpan KeyFetchTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan AutomaticRefreshInterval { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan LastKnownGoodLifetime { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Ticket 6 core knob: the absolute maximum age of the newest <em>successfully retrieved</em>
    /// usable signing-key set that this process will still authenticate against. Measured from the
    /// instant of the last successful usable JWKS retrieval (an actual fetch that yielded at least one
    /// signing key — re-fetching the same key set counts; a failed, malformed, empty or keyless
    /// response does not; a successful token validation does not). Once
    /// <c>now - lastSuccessfulUsableFetch &gt;= MaximumCachedKeyAge</c> the signing keys are withdrawn
    /// and token validation fails closed with a controlled 401 while background refresh attempts keep
    /// running and can restore authentication with no restart.
    ///
    /// This is deliberately distinct from <see cref="LastKnownGoodLifetime"/>: IdentityModel's
    /// <c>LastKnownGoodLifetime</c> bounds only the LKG fallback slot and never expires the current
    /// configuration during a sustained outage, so without this cap stale keys are served forever.
    /// Default: 24 hours.
    /// </summary>
    public TimeSpan MaximumCachedKeyAge { get; set; } = TimeSpan.FromHours(24);

    public bool RequireHttpsMetadata { get; set; } = true;

    public static SupabaseSigningKeyOptions FromConfiguration(IConfiguration configuration)
    {
        var options = new SupabaseSigningKeyOptions();
        configuration.GetSection(SectionName).Bind(options);
        return options;
    }

    /// <summary>
    /// Fail-fast validation applied at startup. Rejects values that are nonsensical or that
    /// IdentityModel would silently clamp / reject, so a misconfiguration surfaces as a boot failure
    /// rather than a subtle auth-availability problem in production.
    /// </summary>
    public void Validate()
    {
        if (KeyFetchTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{SectionName}:KeyFetchTimeout must be positive (was {KeyFetchTimeout}).");
        }

        if (MaximumCachedKeyAge <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{SectionName}:MaximumCachedKeyAge must be positive (was {MaximumCachedKeyAge}).");
        }

        if (AutomaticRefreshInterval < TimeSpan.FromMinutes(5))
        {
            throw new InvalidOperationException(
                $"{SectionName}:AutomaticRefreshInterval must be at least 00:05:00 (was {AutomaticRefreshInterval}).");
        }

        if (RefreshInterval < TimeSpan.FromSeconds(30))
        {
            throw new InvalidOperationException(
                $"{SectionName}:RefreshInterval must be at least 00:00:30 (was {RefreshInterval}).");
        }
    }
}
