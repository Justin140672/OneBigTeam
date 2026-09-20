using System.IdentityModel.Tokens.Jwt;

using HR.Infrastructure.Abstractions;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace HR.Api.Authentication;

/// <summary>
/// P1 "Login as Customer": wiring for the second JwtBearer scheme that validates support-session
/// tokens minted by HR.Infrastructure.Security.SupportSessionTokenIssuer. Deliberately a distinct
/// scheme/issuer/signing key from the real Supabase Bearer scheme (SupabaseJwtBearerConfiguration)
/// — a policy scheme (see Program.cs) forwards each incoming request to whichever scheme matches
/// its token's unvalidated "iss" claim, so both authentication paths can coexist behind a single
/// [Authorize]/Policies(...) surface without weakening the real Supabase validation path at all.
/// </summary>
public static class SupportSessionJwtBearerConfiguration
{
    public const string SchemeName = "SupportSession";

    public static void ConfigureValidation(JwtBearerOptions options, IConfiguration configuration)
    {
        options.MapInboundClaims = false;

        var key = SupportSessionTokenConstants.ResolveSigningKey(configuration);

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = SupportSessionTokenConstants.Issuer,
            ValidateAudience = true,
            ValidAudience = SupportSessionTokenConstants.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            IssuerSigningKey = key,
        };
    }

    /// <summary>
    /// Cheap, unvalidated peek at a bearer token's "iss" claim, used only to pick which of the two
    /// registered JwtBearer schemes should perform the real (signature-validating) authentication
    /// for this request. Never trusted for anything security-relevant by itself — the selected
    /// scheme still fully validates signature/issuer/audience/lifetime before any claim is used.
    /// Returns false (falls back to the real Supabase scheme) for any malformed/non-JWT value.
    /// </summary>
    public static bool LooksLikeSupportSessionToken(string? bearerToken)
    {
        if (string.IsNullOrWhiteSpace(bearerToken))
            return false;

        try
        {
            var handler = new JwtSecurityTokenHandler();
            if (!handler.CanReadToken(bearerToken))
                return false;

            var token = handler.ReadJwtToken(bearerToken);
            return string.Equals(token.Issuer, SupportSessionTokenConstants.Issuer, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }
}
