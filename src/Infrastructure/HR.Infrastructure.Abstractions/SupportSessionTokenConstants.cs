using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Shared issuer/audience/signing-key derivation for the "Login as Customer" support-session JWT.
/// Public (unlike the concrete issuer implementation) because both HR.Infrastructure (issuance —
/// see Security.SupportSessionTokenIssuer) and HR.Api (validation — see
/// SupportSessionJwtBearerConfiguration) need it, and those two projects have no reference to each
/// other. Keeping this the single source of the issuer/audience strings and key-derivation logic
/// guarantees both sides can never drift apart.
/// </summary>
public static class SupportSessionTokenConstants
{
    public const string Issuer = "hr-support-session";
    public const string Audience = "hr-support-session";
    public const string ConfigKey = "SupportSession:SigningKey";

    /// <summary>
    /// Throws if unconfigured — a support session token must never be signed or accepted with a
    /// fallback/default key.
    /// </summary>
    public static SecurityKey ResolveSigningKey(IConfiguration configuration)
    {
        var configured = configuration[ConfigKey];
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"Configuration value '{ConfigKey}' is required to issue or validate support-session tokens.");
        }

        // Configured value may be any length; derive a fixed 256-bit key via SHA-256 so a short
        // configured secret still produces a cryptographically appropriate HMAC key.
        var keyBytes = SHA256.HashData(Encoding.UTF8.GetBytes(configured));
        return new SymmetricSecurityKey(keyBytes);
    }
}
