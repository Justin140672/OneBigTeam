using System.Net.Http;
using HR.Modules.Identity;
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
    public static void ConfigureValidation(
        JwtBearerOptions options, IConfiguration configuration, bool isE2ETesting)
    {
        var supabaseProjectUrl = configuration["SupabaseAuth:ProjectUrl"] ?? "";

        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = $"{supabaseProjectUrl}/auth/v1",
            ValidateAudience = true,
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
            options.TokenValidationParameters.IssuerSigningKeyResolver =
                (_, _, _, _) => [HR.Modules.Identity.Services.E2eFakeSupabaseJwt.SigningKey];
        }

        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("SupabaseJwtBearer")
                    .LogWarning(context.Exception, "Supabase JWT validation failed");
                return Task.CompletedTask;
            },

            // Ticket 13 — cross-tab/cross-replica logout enforcement. This runs AFTER the token has
            // already passed full signature/issuer/audience/lifetime validation, for every request
            // to HR.Api from every caller (HR.Web, HR.Admin.Web, or any other future client) — so
            // this single check point covers all of them at once, and reads the same shared Postgres
            // database every other application instance/replica writes to, so a logout recorded by
            // one replica is honoured by every other replica's very next request. A revoked token
            // fails the request with 401 here, before any endpoint/handler code runs.
            OnTokenValidated = async context =>
            {
                var subClaim = context.Principal?.FindFirst("sub")?.Value;

                if (!Guid.TryParse(subClaim, out var supabaseAuthUserId))
                {
                    return;
                }

                // Deliberately read "iat" off the validated SecurityToken itself, NOT off
                // context.Principal's claims: whether "iat"/"exp"/"nbf" survive into the mapped
                // ClaimsPrincipal depends on which TokenHandler ASP.NET Core's JwtBearerHandler is
                // using (JwtSecurityTokenHandler vs. the newer JsonWebTokenHandler) and its internal
                // claim-mapping rules for registered/reserved claims — confirmed empirically via
                // SessionRevocationEnforcementTests initially failing when read from the principal.
                // Both known SecurityToken implementations expose IssuedAt directly and reliably.
                var tokenIssuedAt = context.SecurityToken switch
                {
                    System.IdentityModel.Tokens.Jwt.JwtSecurityToken jwt =>
                        new DateTimeOffset(jwt.IssuedAt, TimeSpan.Zero),
                    Microsoft.IdentityModel.JsonWebTokens.JsonWebToken jwt =>
                        new DateTimeOffset(jwt.IssuedAt, TimeSpan.Zero),
                    // Unrecognised token type carries no reliably-readable "iat" — fail closed here
                    // would reject every token of that type outright; instead treat it as "now" so
                    // this specific revocation check never blocks it, same fail-open scope as a
                    // missing claim would have had. This branch should never be hit in practice: both
                    // cases above cover every TokenHandler ASP.NET Core's JwtBearerHandler ships with.
                    _ => DateTimeOffset.UtcNow,
                };

                var isRevoked = await context.HttpContext.RequestServices.IsSessionRevokedAsync(
                    supabaseAuthUserId, tokenIssuedAt, context.HttpContext.RequestAborted);

                if (isRevoked)
                {
                    LogRevokedSessionRejected(
                        context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                            .CreateLogger("SupabaseJwtBearer"),
                        context.HttpContext, supabaseAuthUserId, tokenIssuedAt);
                    context.Fail("Session has been revoked.");
                }
            },
        };
    }

    /// <summary>Fixed, server-defined auth event name for a request rejected because its session was revoked.</summary>
    internal const string SessionRevokedAuthEvent = "session_revoked";

    internal const string UnmatchedRoute = "(unmatched)";

    internal static void LogRevokedSessionRejected(
        ILogger logger, HttpContext httpContext, Guid supabaseAuthUserId, DateTimeOffset tokenIssuedAt) =>
        logger.LogInformation(
            "Rejected request for a revoked session. AuthEvent={AuthEvent} Sub={Sub} TokenIssuedAt={TokenIssuedAt} RouteTemplate={RouteTemplate}",
            SessionRevokedAuthEvent, supabaseAuthUserId, tokenIssuedAt, ResolveRouteTemplate(httpContext));

    internal static string ResolveRouteTemplate(HttpContext httpContext) =>
        (httpContext.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? UnmatchedRoute;

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

        var httpClient = new HttpClient { Timeout = keyOptions.KeyFetchTimeout };
        var documentRetriever = new HttpDocumentRetriever(httpClient)
        {
            RequireHttps = keyOptions.RequireHttpsMetadata,
        };

        var gate = new FreshnessGatedConfigurationManager(keyOptions, clock, logger);

        var retriever = new FreshnessTrackingRetriever(
            new SupabaseJwksRetriever(logger), gate.RecordUsableRetrieval);

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

        options.TokenValidationParameters.IssuerSigningKeyResolver =
            (_, _, _, _) => gate.ResolveUsableSigningKeys();

        options.TokenValidationParameters.IssuerSigningKeyValidator =
            (key, _, _) => gate.IsWithinMaximumCachedKeyAge();
    }
}

internal sealed record FreshnessSnapshot(OpenIdConnectConfiguration Configuration, DateTimeOffset RetrievedAt);

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
        var configuration = await _inner.GetConfigurationAsync(address, retriever, cancel).ConfigureAwait(false);
        if (configuration.SigningKeys.Count > 0)
        {
            _onUsableRetrieval(configuration);
        }

        return configuration;
    }
}

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

    public void RecordUsableRetrieval(OpenIdConnectConfiguration configuration)
        => _snapshot = new FreshnessSnapshot(configuration, _timeProvider.GetUtcNow());

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
        if (_snapshot is { } current
            && _timeProvider.GetUtcNow() - current.RetrievedAt >= _maximumCachedKeyAge)
        {
            _inner!.RequestRefresh();
        }

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

            var keySet = new JsonWebKeySet(json);

            var configuration = new OpenIdConnectConfiguration { JsonWebKeySet = keySet };
            foreach (var key in keySet.GetSigningKeys())
            {
                configuration.SigningKeys.Add(key);
            }

            if (configuration.SigningKeys.Count == 0)
            {
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
