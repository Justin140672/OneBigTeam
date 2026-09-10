using System.Net.Http;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace HR.Api.Authentication;

/// <summary>
/// Centralises Supabase JWT bearer validation wiring (Ticket 6) so the real pipeline used by
/// <c>Program.cs</c> can also be exercised verbatim by <c>SigningKeyRefreshResilienceTests</c>.
///
/// The real path resolves signing keys via the established asynchronous
/// <see cref="ConfigurationManager{T}"/> mechanism fed by <see cref="SupabaseJwksRetriever"/> — a
/// custom <see cref="IConfigurationRetriever{T}"/> that turns Supabase's <em>bare</em> JWKS document
/// (RFC 7517) into an <see cref="OpenIdConnectConfiguration"/>. Supabase does not serve a usable
/// OpenID Connect discovery document, so <see cref="JwtBearerOptions.MetadataAddress"/> auto-discovery
/// is not an option.
///
/// <see cref="ConfigurationManager{T}"/> supplies, correctly and for free: non-blocking async
/// refresh, a single in-flight refresh per key source (<c>SemaphoreSlim(1,1)</c>),
/// <c>AutomaticRefreshInterval</c> / <c>RefreshInterval</c> (the latter throttling refresh storms from
/// unknown <c>kid</c> values), and <c>LastKnownGoodConfiguration</c> / <c>LastKnownGoodLifetime</c>
/// for outage behaviour. The <see cref="JwtBearerHandler"/> performs the single bounded
/// <c>RequestRefresh()</c> + retry on an unknown signing key — never a per-request synchronous fetch.
/// </summary>
public static class SupabaseJwtBearerConfiguration
{
    /// <summary>
    /// Configures token validation parameters (issuer, audience, lifetime, signature, pinned
    /// algorithms) and failure logging. Does <em>not</em> attach the
    /// <see cref="ConfigurationManager{T}"/> — call <see cref="AttachConfigurationManager"/> afterwards
    /// (kept separate so it can log through <see cref="ILoggerFactory"/> from DI).
    /// </summary>
    public static void ConfigureValidation(
        JwtBearerOptions options, IConfiguration configuration, bool isE2ETesting)
    {
        var supabaseProjectUrl = configuration["SupabaseAuth:ProjectUrl"] ?? "";

        // Without this, JwtBearerHandler remaps short JWT claim names ("sub", "email") to legacy
        // long-form ClaimTypes URIs; the Identity module reads them literally.
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = $"{supabaseProjectUrl}/auth/v1",
            ValidateAudience = true,
            // "authenticated" is Supabase's well-known audience for issued access tokens.
            ValidAudience = "authenticated",
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            // Ticket 6 hardening: pin accepted signing algorithms. The real path only accepts
            // asymmetric Supabase tokens (ES256 / RS256); the E2E path only accepts the locally
            // minted HS256 token. Without this an attacker-chosen algorithm ("none", or HS256 keyed
            // off a public key) would be considered.
            ValidAlgorithms = isE2ETesting
                ? [SecurityAlgorithms.HmacSha256]
                : [SecurityAlgorithms.EcdsaSha256, SecurityAlgorithms.RsaSha256],
        };

        if (isE2ETesting)
        {
            // Every token presented under E2E_TESTING was minted locally by E2eFakeSupabaseJwt and
            // signed with a fixed, non-secret symmetric key — never by the real Supabase project.
            // Resolve that one key directly and never touch the network.
            options.TokenValidationParameters.IssuerSigningKeyResolver =
                (_, _, _, _) => [HR.Modules.Identity.Services.E2eFakeSupabaseJwt.SigningKey];
        }

        // The default Serilog "Microsoft" Warning override swallows JwtBearerHandler's own
        // authentication-failure logs, so a validation failure otherwise surfaces only as a bare 401.
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("SupabaseJwtBearer")
                    .LogWarning(context.Exception, "Supabase JWT validation failed");
                return Task.CompletedTask;
            },
        };
    }

    /// <summary>
    /// Attaches an async <see cref="ConfigurationManager{T}"/> for the real Supabase JWKS endpoint,
    /// wrapped in a <see cref="FreshnessGatedConfigurationManager"/> that enforces
    /// <see cref="SupabaseSigningKeyOptions.MaximumCachedKeyAge"/> on every key-supplying path.
    /// No-op under E2E testing (local HS256 key, never network) and when no JWKS URL is configured.
    /// </summary>
    /// <param name="timeProvider">
    /// Clock for the freshness / max-age / forced-refresh-throttle logic. Production passes
    /// <c>null</c> (=> <see cref="TimeProvider.System"/>); tests inject a fake.
    /// </param>
    public static void AttachConfigurationManager(
        JwtBearerOptions options,
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        bool isE2ETesting,
        TimeProvider? timeProvider = null)
    {
        if (isE2ETesting)
        {
            return;
        }

        var jwksUrl = configuration["SupabaseAuth:JwksUrl"] ?? "";
        if (string.IsNullOrWhiteSpace(jwksUrl))
        {
            return;
        }

        var keyOptions = SupabaseSigningKeyOptions.FromConfiguration(configuration);
        keyOptions.Validate();

        var logger = loggerFactory.CreateLogger("SupabaseJwks");
        var clock = timeProvider ?? TimeProvider.System;

        // HttpClient.Timeout is the bounded wait for a single key fetch: the request is aborted after
        // KeyFetchTimeout, surfacing as a fetch failure rather than a hang on the request thread.
        var httpClient = new HttpClient { Timeout = keyOptions.KeyFetchTimeout };
        var documentRetriever = new HttpDocumentRetriever(httpClient)
        {
            RequireHttps = keyOptions.RequireHttpsMetadata,
        };

        var gate = new FreshnessGatedConfigurationManager(keyOptions, clock, logger);

        // The freshness timestamp is renewed ONLY from inside the retriever, i.e. only when an actual
        // network fetch returns a usable key set (SupabaseJwksRetriever throws on malformed / empty /
        // keyless, so a normal return here always means "usable"). Cached hits and token validations
        // never run this code, so they never renew freshness.
        var retriever = new FreshnessTrackingRetriever(
            new SupabaseJwksRetriever(logger), gate.RecordUsableRetrieval);

        // Inner manager keeps its documented duties (async non-blocking fetch, single in-flight
        // refresh per source, AutomaticRefreshInterval background refresh, LKG slot). Its
        // RefreshInterval is pinned low: the forced-refresh throttle that caps unknown-kid storms is
        // owned by the gate (so it can use the injected clock and the configured RefreshInterval).
        var inner = new ConfigurationManager<OpenIdConnectConfiguration>(
            jwksUrl, retriever, documentRetriever)
        {
            AutomaticRefreshInterval = keyOptions.AutomaticRefreshInterval,
            RefreshInterval = TimeSpan.FromSeconds(1),
            LastKnownGoodLifetime = keyOptions.LastKnownGoodLifetime,
        };
        gate.AttachInner(inner);

        options.ConfigurationManager = gate;
        options.TokenValidationParameters.ConfigurationManager = gate;

        // Authoritative per-validation freshness gate: IdentityModel calls this on every signature
        // check. It hands back the cached keys only while they are inside MaximumCachedKeyAge and an
        // empty set otherwise, so a stale cache fails token validation closed regardless of any
        // configuration caching inside JwtBearerHandler. The ConfigurationManager above still owns the
        // refresh-and-retry on an unknown kid and the background refresh loop.
        options.TokenValidationParameters.IssuerSigningKeyResolver =
            (_, _, _, _) => gate.ResolveUsableSigningKeys();

        // Final, source-independent freshness check: IdentityModel invokes this for the signing key it
        // actually used to verify the token signature — whatever supplied it (resolver, cloned
        // TokenValidationParameters, cached ConfigurationManager state). Rejecting here once the cache
        // is past MaximumCachedKeyAge guarantees validation fails closed even if a key leaks through
        // JwtBearerHandler's own configuration caching.
        options.TokenValidationParameters.IssuerSigningKeyValidator =
            (key, _, _) => gate.IsWithinMaximumCachedKeyAge();
    }
}

/// <summary>
/// Immutable snapshot of the newest successfully retrieved usable signing-key configuration and the
/// clock instant it was retrieved. Published by reference swap so readers always see a consistent
/// pair.
/// </summary>
internal sealed record FreshnessSnapshot(OpenIdConnectConfiguration Configuration, DateTimeOffset RetrievedAt);

/// <summary>
/// Decorates the real <see cref="IConfigurationRetriever{T}"/> so the enclosing gate learns the exact
/// instant of every successful usable retrieval — including a re-fetch that returns the same keys.
/// Never invoked for cached hits or token validations, so those can never renew freshness.
/// </summary>
internal sealed class FreshnessTrackingRetriever : IConfigurationRetriever<OpenIdConnectConfiguration>
{
    private readonly IConfigurationRetriever<OpenIdConnectConfiguration> _inner;
    private readonly Action<OpenIdConnectConfiguration> _onUsableRetrieval;

    public FreshnessTrackingRetriever(
        IConfigurationRetriever<OpenIdConnectConfiguration> inner,
        Action<OpenIdConnectConfiguration> onUsableRetrieval)
    {
        _inner = inner;
        _onUsableRetrieval = onUsableRetrieval;
    }

    public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(
        string address, IDocumentRetriever retriever, CancellationToken cancel)
    {
        // Throws on transport failure / non-2xx / malformed / empty / keyless — those propagate and
        // never reach the callback, so freshness is not renewed on a bad response.
        var configuration = await _inner.GetConfigurationAsync(address, retriever, cancel).ConfigureAwait(false);
        if (configuration.SigningKeys.Count > 0)
        {
            _onUsableRetrieval(configuration);
        }

        return configuration;
    }
}

/// <summary>
/// Interposition point on the Supabase signing-key <see cref="ConfigurationManager{T}"/>.
///
/// <see cref="JwtBearerHandler"/> reaches signing keys through two members of this type — the generic
/// <see cref="IConfigurationManager{T}"/> (<see cref="GetConfigurationAsync"/>, used to pre-populate
/// <c>TokenValidationParameters.IssuerSigningKeys</c>) and the <see cref="BaseConfigurationManager"/>
/// surface (<see cref="GetBaseConfigurationAsync"/> + <see cref="RequestRefresh"/>, used by the
/// IdentityModel signature validator for the single bounded refresh-and-retry on an unknown key).
/// Both are routed through <see cref="GetGatedConfigurationAsync"/>. This type never populates its own
/// <see cref="BaseConfigurationManager.LastKnownGoodConfiguration"/> slot, so the validator's LKG
/// branch is inert and there is exactly one place that can hand out keys.
///
/// Behaviour: delegate to the inner manager (so real refreshes, single-flight and the LKG slot keep
/// working), then withhold all signing keys once
/// <c>now - lastSuccessfulUsableFetch &gt;= MaximumCachedKeyAge</c>. Withholding yields a controlled
/// 401 (empty key set) rather than a 500, and refresh attempts keep running so a later successful
/// fetch restores authentication with no restart.
/// </summary>
internal sealed class FreshnessGatedConfigurationManager
    : BaseConfigurationManager, IConfigurationManager<OpenIdConnectConfiguration>
{
    private static readonly OpenIdConnectConfiguration EmptyConfiguration = new();

    private readonly TimeSpan _maximumCachedKeyAge;
    private readonly TimeSpan _forcedRefreshInterval;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;

    private ConfigurationManager<OpenIdConnectConfiguration>? _inner;
    private volatile FreshnessSnapshot? _snapshot;
    private long _lastForcedRefreshTicks = long.MinValue;

    public FreshnessGatedConfigurationManager(
        SupabaseSigningKeyOptions options, TimeProvider timeProvider, ILogger logger)
    {
        _maximumCachedKeyAge = options.MaximumCachedKeyAge;
        _forcedRefreshInterval = options.RefreshInterval;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    internal void AttachInner(ConfigurationManager<OpenIdConnectConfiguration> inner) => _inner = inner;

    /// <summary>
    /// Callback for <see cref="FreshnessTrackingRetriever"/>. Publishes a new
    /// <see cref="FreshnessSnapshot"/> by reference swap — scoped to this manager instance, so each
    /// key source tracks its own freshness. Renewed on every usable retrieval, including a re-fetch
    /// that returns an unchanged key set.
    /// </summary>
    public void RecordUsableRetrieval(OpenIdConnectConfiguration configuration)
        => _snapshot = new FreshnessSnapshot(configuration, _timeProvider.GetUtcNow());

    /// <summary>
    /// Signing keys currently usable for token validation: the newest successfully retrieved set
    /// while it is within <see cref="SupabaseSigningKeyOptions.MaximumCachedKeyAge"/>, otherwise none.
    /// </summary>
    public IEnumerable<SecurityKey> ResolveUsableSigningKeys()
    {
        var snapshot = _snapshot;
        if (snapshot is null)
        {
            return Array.Empty<SecurityKey>();
        }

        if (_timeProvider.GetUtcNow() - snapshot.RetrievedAt >= _maximumCachedKeyAge)
        {
            return Array.Empty<SecurityKey>();
        }

        return snapshot.Configuration.SigningKeys;
    }

    /// <summary>
    /// True while the newest successfully retrieved usable key set is still inside
    /// <see cref="SupabaseSigningKeyOptions.MaximumCachedKeyAge"/>. Used as the last-resort
    /// <see cref="TokenValidationParameters.IssuerSigningKeyValidator"/> so a stale cache fails token
    /// validation closed regardless of where the signing key was sourced.
    /// </summary>
    public bool IsWithinMaximumCachedKeyAge()
    {
        var snapshot = _snapshot;
        return snapshot is not null
            && _timeProvider.GetUtcNow() - snapshot.RetrievedAt < _maximumCachedKeyAge;
    }

    public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel)
        => await GetGatedConfigurationAsync(cancel).ConfigureAwait(false);

    public override async Task<BaseConfiguration> GetBaseConfigurationAsync(CancellationToken cancel)
        => await GetGatedConfigurationAsync(cancel).ConfigureAwait(false);

    public override void RequestRefresh()
    {
        // Forced-refresh throttle (unknown-kid storm cap), on the injected clock. At most one honoured
        // RequestRefresh per RefreshInterval; extra calls in the window are dropped.
        var now = _timeProvider.GetUtcNow().UtcTicks;
        var last = Interlocked.Read(ref _lastForcedRefreshTicks);
        if (last != long.MinValue && now - last < _forcedRefreshInterval.Ticks)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _lastForcedRefreshTicks, now, last) != last)
        {
            return;
        }

        _inner?.RequestRefresh();
    }

    private async Task<OpenIdConnectConfiguration> GetGatedConfigurationAsync(CancellationToken cancel)
    {
        // Always pump the inner manager: this is what performs the actual (non-blocking) fetch on
        // cold start, honours a pending RequestRefresh, runs AutomaticRefreshInterval background
        // refreshes and maintains single-flight. A sustained outage throws here once no cached
        // config exists; otherwise it returns the last good config.
        try
        {
            await _inner!.GetBaseConfigurationAsync(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Supabase signing-key refresh attempt failed: {ErrorType} {ErrorMessage}",
                ex.GetType().Name, ex.Message);
        }

        var snapshot = _snapshot;
        if (snapshot is null)
        {
            // Never obtained a usable key set — fail closed.
            return EmptyConfiguration;
        }

        var age = _timeProvider.GetUtcNow() - snapshot.RetrievedAt;
        if (age >= _maximumCachedKeyAge)
        {
            _logger.LogWarning(
                "Supabase signing keys exceeded MaximumCachedKeyAge ({MaxAge}); withholding keys and "
                + "failing token validation closed until a refresh succeeds.",
                _maximumCachedKeyAge);
            return EmptyConfiguration;
        }

        return snapshot.Configuration;
    }
}

/// <summary>
/// Custom <see cref="IConfigurationRetriever{T}"/> that turns Supabase's bare JWKS JSON into an
/// <see cref="OpenIdConnectConfiguration"/>. There is no discovery document to parse, so only the
/// signing keys are populated.
/// </summary>
internal sealed class SupabaseJwksRetriever : IConfigurationRetriever<OpenIdConnectConfiguration>
{
    private readonly ILogger _logger;

    public SupabaseJwksRetriever(ILogger logger) => _logger = logger;

    public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(
        string address, IDocumentRetriever retriever, CancellationToken cancel)
    {
        try
        {
            var json = await retriever.GetDocumentAsync(address, cancel).ConfigureAwait(false);

            // Throws on malformed JSON — caught below and rethrown so ConfigurationManager keeps
            // serving the last-known-good configuration instead of partially trusting garbage.
            var keySet = new JsonWebKeySet(json);

            var configuration = new OpenIdConnectConfiguration { JsonWebKeySet = keySet };
            foreach (var key in keySet.GetSigningKeys())
            {
                configuration.SigningKeys.Add(key);
            }

            if (configuration.SigningKeys.Count == 0)
            {
                // Syntactically valid but key-less document is unusable — treat as a failure so the
                // previous good keys keep being used.
                throw new InvalidOperationException("Supabase JWKS document contained no signing keys.");
            }

            return configuration;
        }
        catch (Exception ex)
        {
            // Logging rules: message + exception type only. A JWKS fetch/parse failure carries no
            // token, credential or PII material; kept deliberately minimal regardless.
            _logger.LogWarning(
                "Supabase signing-key configuration retrieval failed: {ErrorType} {ErrorMessage}",
                ex.GetType().Name, ex.Message);
            throw;
        }
    }
}
