using System.Collections.Frozen;

using Microsoft.AspNetCore.RateLimiting;

namespace HR.Api.RateLimiting;

internal static class RateLimitRejectionLogging
{
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
