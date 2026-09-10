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
    /// <summary>Configuration section these options bind from.</summary>
    public const string SectionName = "SupabaseAuth:SigningKeyRefresh";

    /// <summary>
    /// Ticket knob: <c>Key-fetch timeout</c>. Applied as <see cref="System.Net.Http.HttpClient.Timeout"/>
    /// on the JWKS document retriever, bounding how long a single key fetch can take before it is
    /// treated as a failure. Default: 5 seconds.
    /// </summary>
    public TimeSpan KeyFetchTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Ticket knob: <c>Normal refresh interval</c>. Maps to
    /// <c>ConfigurationManager.AutomaticRefreshInterval</c> — how often the manager proactively
    /// refreshes keys in the background during normal operation (picks up rotated / standby keys with
    /// no restart). Library minimum is 5 minutes. Default: 15 minutes.
    /// </summary>
    public TimeSpan AutomaticRefreshInterval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Ticket knob: <c>Minimum interval between forced refreshes</c>. Maps to
    /// <c>ConfigurationManager.RefreshInterval</c> — the floor between honoured
    /// <c>RequestRefresh()</c> calls, which is what caps a refresh storm triggered by repeated unknown
    /// <c>kid</c> values. Library minimum is 30 seconds. Default: 30 seconds.
    /// </summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maps to <c>ConfigurationManager.LastKnownGoodLifetime</c>. This ONLY governs IdentityModel's
    /// last-known-good <em>fallback</em> slot: how long a configuration that has been demoted to LKG
    /// (because a later refresh produced a different/!usable result) may still be consulted as a
    /// secondary signature source. It does <b>not</b> expire the manager's <em>current</em>
    /// configuration: when every refresh attempt fails, IdentityModel keeps serving the last
    /// successfully fetched keys from the current slot indefinitely — so on its own this knob lets
    /// keys outlive any real freshness guarantee. Enforcing an absolute cap is the job of
    /// <see cref="MaximumCachedKeyAge"/>. Default: 24 hours.
    /// </summary>
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

    /// <summary>
    /// Whether the JWKS endpoint must be served over HTTPS. Always <c>true</c> in real environments;
    /// only relaxed to <c>false</c> by the in-process controlled key server used in
    /// <c>SigningKeyRefreshResilienceTests</c>.
    /// </summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>Binds a fresh instance from configuration, falling back to the defaults above.</summary>
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

        // IdentityModel's ConfigurationManager rejects an AutomaticRefreshInterval below 5 minutes.
        if (AutomaticRefreshInterval < TimeSpan.FromMinutes(5))
        {
            throw new InvalidOperationException(
                $"{SectionName}:AutomaticRefreshInterval must be at least 00:05:00 (was {AutomaticRefreshInterval}).");
        }

        // RefreshInterval is the forced-refresh throttle floor; keep it at/above 30 seconds so a
        // burst of unknown-kid tokens cannot turn into a JWKS fetch storm.
        if (RefreshInterval < TimeSpan.FromSeconds(30))
        {
            throw new InvalidOperationException(
                $"{SectionName}:RefreshInterval must be at least 00:00:30 (was {RefreshInterval}).");
        }
    }
}
