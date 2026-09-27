using System.Collections.Frozen;

using Microsoft.AspNetCore.RateLimiting;

namespace HR.Api.RateLimiting;

/// <summary>
/// CodeQL #60 (log forging): the 429 rejection log line used to include
/// <c>HttpContext.Request.Path</c>, which is caller-controlled request-target text (it can carry
/// percent-decoded control characters). The log line now identifies the rejection only by the
/// server-defined rate-limit policy name taken from the matched endpoint's
/// <see cref="EnableRateLimitingAttribute"/> metadata, and only when that name is one of the
/// policies this application registers — anything else is reported as <see cref="UnknownPolicy"/>.
/// The raw path, query string and any other request-target text are never logged here.
/// </summary>
internal static class RateLimitRejectionLogging
{
    /// <summary>Per-IP policy for the public marketing contact form (registered in Program.cs).</summary>
    public const string ContactFormPolicy = "contact-form";

    public const string UnknownPolicy = "unknown";

    private static readonly FrozenSet<string> KnownPolicies = new[]
    {
        ContactFormPolicy,
        IdentityRateLimiting.LoginPolicy,
        IdentityRateLimiting.SignUpPolicy,
        IdentityRateLimiting.ForgotPasswordPolicy,
        IdentityRateLimiting.ResendVerificationPolicy,
        IdentityRateLimiting.AcceptInvitePolicy,
        IdentityRateLimiting.ResetPasswordPolicy,
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Returns the matched endpoint's rate-limit policy name when it is a policy registered by this
    /// application, otherwise <see cref="UnknownPolicy"/>. Never derived from the request itself.
    /// </summary>
    public static string ResolvePolicyName(HttpContext httpContext)
    {
        var policyName = httpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
        return policyName is not null && KnownPolicies.Contains(policyName) ? policyName : UnknownPolicy;
    }

    public static void LogRejected(ILogger logger, HttpContext httpContext) =>
        logger.LogWarning(
            "Rate limit rejected request. RateLimitPolicy={RateLimitPolicy}",
            ResolvePolicyName(httpContext));
}
