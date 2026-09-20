using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;

using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;

namespace HR.Api.RateLimiting;

/// <summary>
/// P1 abuse protection for anonymous identity endpoints (Login, Sign-up, Forgot password, Resend
/// verification, Accept invitation, Reset password). Each endpoint gets its own named,
/// fixed-window rate-limit policy partitioned on a composite key of:
///   - the caller's client IP (resolved only from a trusted proxy chain — see
///     <see cref="ConfigureTrustedProxies"/>), and
///   - for endpoints that carry an email or single-use token, a salted SHA-256 hash of that
///     normalized value — never the raw value, so a limiter key is never itself sensitive data
///     that could leak through logs/metrics.
///
/// Design note: ASP.NET Core's rate-limiting middleware resolves exactly one active named policy
/// per endpoint (the last <c>RequireRateLimiting</c>/<c>[EnableRateLimiting]</c> metadata entry
/// wins), so independently-capped IP and identity ceilings cannot both be enforced as two
/// separately-named policies on the same endpoint. A single composite-key partition is used
/// instead: an attacker fixing their IP while spraying many different destination emails (or vice
/// versa) still exhausts their own combination quickly, and a legitimate caller retrying the same
/// email from the same IP is bounded exactly as intended — but a determined attacker rotating BOTH
/// dimensions in lockstep is bounded only by the shared window, not by two independent ceilings.
/// Closing that residual gap would require either a custom <see cref="RateLimiter"/> composing two
/// independent <see cref="PartitionedRateLimiter{TResource}"/> instances at acquire time, or
/// upstream API support for stacking policies — flagged as a follow-up rather than attempted here.
///
/// This app is deployed as a single application instance per environment (see
/// specifications/architecture/08-deployment-architecture.md, "Single Application Deployment") —
/// the in-memory limiter state here is not multiplied across replicas for that documented
/// deployment shape. If that ever changes, these limiters would need to move to a shared/distributed
/// store (e.g. Postgres- or Redis-backed) to avoid each instance granting its own independent quota.
/// </summary>
internal static class IdentityRateLimiting
{
    public const string LoginPolicy = "identity-login";
    public const string SignUpPolicy = "identity-signup";
    public const string ForgotPasswordPolicy = "identity-forgot-password";
    public const string ResendVerificationPolicy = "identity-resend-verification";
    public const string AcceptInvitePolicy = "identity-accept-invite";
    public const string ResetPasswordPolicy = "identity-reset-password";

    /// <summary>
    /// Buffers and parses the request body once (from behind <see cref="app.UseRateLimiter"/>, i.e.
    /// before FastEndpoints ever sees it) purely to extract a normalized email/token for the keyed
    /// partition above — never for any other purpose, and the raw value is never itself retained
    /// past this middleware; only its salted hash survives into the limiter key. Scoped to exactly
    /// the six identity POST routes so no other request pays for the body buffering. Fails open to
    /// "no secondary key" (the request is still admitted/denied purely on the IP component) on any
    /// parse failure — a malformed body is FluentValidation's problem, not this middleware's.
    /// </summary>
    private static readonly IReadOnlyDictionary<PathString, string> RouteToBodyField = new Dictionary<PathString, string>
    {
        ["/api/login"] = "email",
        ["/api/signup"] = "adminEmail",
        ["/api/forgot-password"] = "email",
        ["/api/resend-verification"] = "email",
        ["/api/invites/accept"] = "token",
        ["/api/reset-password"] = "accessToken",
    };

    public const string SecondaryKeyItemKey = "__identity_rate_limit_secondary_key";

    public static async Task ExtractSecondaryRateLimitKeyAsync(HttpContext context, RequestDelegate next)
    {
        if (HttpMethods.IsPost(context.Request.Method)
            && RouteToBodyField.TryGetValue(context.Request.Path, out var fieldName))
        {
            try
            {
                context.Request.EnableBuffering();
                using var reader = new StreamReader(
                    context.Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false,
                    bufferSize: 4096, leaveOpen: true);
                var body = await reader.ReadToEndAsync(context.RequestAborted);
                context.Request.Body.Position = 0;

                using var document = JsonDocument.Parse(body);
                if (document.RootElement.TryGetProperty(fieldName, out var valueElement)
                    && valueElement.ValueKind == JsonValueKind.String)
                {
                    var raw = valueElement.GetString();
                    if (!string.IsNullOrWhiteSpace(raw))
                    {
                        var normalized = fieldName is "token" or "accessToken"
                            ? raw.Trim()
                            : raw.Trim().ToLowerInvariant();
                        context.Items[SecondaryKeyItemKey] = KeyedHash(normalized);
                    }
                }
            }
            catch (JsonException)
            {
                // Fails open — see method remarks.
            }
        }

        await next(context);
    }

    /// <summary>
    /// Salted (never a bare/reversible hash of the value alone) SHA-256 digest used as a rate-limit
    /// partition key component. The salt is a fixed, non-secret, process-local value — its only
    /// purpose is to stop a limiter key being trivially recomputable/correlatable from a leaked hash
    /// list against a plain email dictionary; it is not a security boundary in itself (this is a
    /// rate limit, not an authentication credential).
    /// </summary>
    private static string KeyedHash(string value)
    {
        var bytes = Encoding.UTF8.GetBytes("identity-rate-limit-salt:" + value);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private static string ClientIpKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown-ip";

    private static string CompositeKey(HttpContext context, string policyName)
    {
        var secondary = context.Items.TryGetValue(SecondaryKeyItemKey, out var value) && value is string key
            ? key
            : "no-identity-key";

        return $"{policyName}:{ClientIpKey(context)}:{secondary}";
    }

    public static void AddIdentityRateLimiting(this RateLimiterOptions options, IConfiguration configuration)
    {
        AddPolicy(options, configuration, LoginPolicy, windowMinutes: 1, permitLimit: 8);
        AddPolicy(options, configuration, SignUpPolicy, windowMinutes: 10, permitLimit: 5);
        AddPolicy(options, configuration, ForgotPasswordPolicy, windowMinutes: 15, permitLimit: 5);
        AddPolicy(options, configuration, ResendVerificationPolicy, windowMinutes: 15, permitLimit: 5);
        AddPolicy(options, configuration, AcceptInvitePolicy, windowMinutes: 15, permitLimit: 10);
        AddPolicy(options, configuration, ResetPasswordPolicy, windowMinutes: 15, permitLimit: 10);

        // Safe error contract + Retry-After on 429: never leaks the identity/policy internals a
        // caller could use to fingerprint which specific dimension (IP vs identity) tripped.
        // Metrics/log line reports policy name and outcome only — never the request's identity value.
        options.OnRejected = async (context, cancellationToken) =>
        {
            context.HttpContext.Response.Headers.RetryAfter =
                context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                    ? ((int)retryAfter.TotalSeconds).ToString()
                    : "60";

            context.HttpContext.Response.ContentType = "application/json";
            await context.HttpContext.Response.WriteAsJsonAsync(
                new { error = "Too many requests. Please try again later.", code = "rate_limited" },
                cancellationToken);

            var logger = context.HttpContext.RequestServices
                .GetRequiredService<ILoggerFactory>().CreateLogger("IdentityRateLimiting");
            logger.LogWarning(
                "Rate limit rejected request. Path={Path}", context.HttpContext.Request.Path);
        };
    }

    private static void AddPolicy(
        RateLimiterOptions options,
        IConfiguration configuration,
        string policyName,
        int windowMinutes,
        int permitLimit)
    {
        // Configurable per policy/environment (dev/test can raise limits without disabling the
        // production policy — see acceptance criteria) via
        // Identity:RateLimits:<PolicyName>:{WindowMinutes,PermitLimit}. WindowSeconds is an
        // additional, optional override (test-motivated: minutes-only granularity makes a
        // deterministic window-recovery integration test impractically slow) — when present it wins
        // over WindowMinutes; production config continues to use WindowMinutes only.
        var section = configuration.GetSection($"Identity:RateLimits:{policyName}");
        var resolvedWindow = section.GetValue("WindowMinutes", windowMinutes);
        var resolvedWindowSeconds = section.GetValue<int?>("WindowSeconds", null);
        var resolvedPermit = section.GetValue("PermitLimit", permitLimit);

        options.AddPolicy<string>(policyName, context =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: CompositeKey(context, policyName),
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    Window = resolvedWindowSeconds is int seconds
                        ? TimeSpan.FromSeconds(seconds)
                        : TimeSpan.FromMinutes(resolvedWindow),
                    PermitLimit = resolvedPermit,
                    QueueLimit = 0,
                }));
    }

    /// <summary>
    /// Forwarded-header trust: <see cref="ForwardedHeadersOptions"/> only honours X-Forwarded-For
    /// from a request whose immediate remote address is itself in <c>KnownProxies</c>/<c>KnownIPNetworks</c>
    /// — an untrusted client cannot spoof its own IP (and therefore cannot pick its own rate-limit
    /// partition) merely by sending the header. Configured via Identity:TrustedProxies (comma-
    /// separated IP list) and Identity:TrustedProxyNetworks (comma-separated CIDR list); empty by
    /// default, meaning forwarded headers are ignored entirely (fail-closed to the raw connection
    /// IP, which is always correct for a direct-to-app deployment).
    /// </summary>
    public static void ConfigureTrustedProxies(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();

            foreach (var address in (configuration["Identity:TrustedProxies"] ?? "")
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (System.Net.IPAddress.TryParse(address, out var ip))
                    options.KnownProxies.Add(ip);
            }

            foreach (var cidr in (configuration["Identity:TrustedProxyNetworks"] ?? "")
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = cidr.Split('/');
                if (parts.Length == 2
                    && System.Net.IPAddress.TryParse(parts[0], out var network)
                    && int.TryParse(parts[1], out var prefixLength))
                {
                    options.KnownIPNetworks.Add(new System.Net.IPNetwork(network, prefixLength));
                }
            }

            // CRITICAL: ForwardedHeadersMiddleware's own documented behaviour is the OPPOSITE of
            // what "empty allow-list" would naturally suggest — when KnownProxies AND
            // KnownIPNetworks are BOTH empty, it treats that as "no restriction configured" and
            // honours X-Forwarded-For from ANY caller, not from none. Left as the two empty lists
            // above, an unconfigured deployment would let any anonymous caller spoof its own
            // rate-limit IP partition merely by sending the header — silently defeating this
            // entire feature's IP-based defence. Explicitly disabling header processing when
            // nothing is configured is what actually gives the documented "empty = ignored,
            // fail-closed to the raw connection IP" behaviour.
            if (options.KnownProxies.Count == 0 && options.KnownIPNetworks.Count == 0)
            {
                options.ForwardedHeaders = ForwardedHeaders.None;
            }
        });
    }
}
