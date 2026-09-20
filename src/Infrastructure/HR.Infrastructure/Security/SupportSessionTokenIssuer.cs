using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

using HR.Infrastructure.Abstractions;

using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace HR.Infrastructure.Security;

/// <summary>
/// P1 "Login as Customer": issues the locally-signed (HMAC-SHA256) support-session access token.
/// Deliberately a distinct signer/issuer from real Supabase-issued tokens — see
/// ISupportSessionTokenIssuer's remarks. HR.Api validates this same token via a second JwtBearer
/// scheme reading the identical SupportSessionTokenConstants.ConfigKey configuration value
/// (SupportSessionJwtBearerConfiguration in HR.Api); both sides share
/// HR.Infrastructure.Abstractions.SupportSessionTokenConstants for the issuer/audience/key
/// derivation so they can never drift apart despite having no reference to each other.
/// </summary>
internal sealed class SupportSessionTokenIssuer(IConfiguration configuration) : ISupportSessionTokenIssuer
{
    public string IssueToken(Guid supportSessionId, Guid companyId, Guid adminUserId, string adminEmail, DateTimeOffset expiresAt)
    {
        var key = SupportSessionTokenConstants.ResolveSigningKey(configuration);

        var handler = new JwtSecurityTokenHandler();

        var token = new JwtSecurityToken(
            issuer: SupportSessionTokenConstants.Issuer,
            audience: SupportSessionTokenConstants.Audience,
            claims:
            [
                // The acting platform administrator's own Supabase user id — never resolved
                // against identity.user_profiles for a support-session token (see
                // SupabaseCurrentUserResolutionMiddleware), but used as every audit event's
                // ActorUserId during the session so the audit trail correctly attributes to the
                // real administrator (see ISupportSessionTokenIssuer's remarks).
                new Claim("sub", adminUserId.ToString()),
                new Claim("email", adminEmail),
                new Claim("company_id", companyId.ToString()),
                new Claim("support_session_id", supportSessionId.ToString()),
            ],
            notBefore: DateTime.UtcNow,
            expires: expiresAt.UtcDateTime,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return handler.WriteToken(token);
    }
}
