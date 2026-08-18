using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace HR.Modules.Identity.Services;

/// <summary>
/// Locally-signed (symmetric HMAC-SHA256) JWT issuance used ONLY when E2E_TESTING=true, so E2E sign-in
/// (<see cref="FakeSupabaseAuthGateway"/>) no longer needs a real Supabase Auth password-grant call —
/// confirmed via live retry evidence (5 attempts, growing backoff up to 25s, still failing 3-in-a-row
/// for multiple personas in the same run) that Supabase's own Auth API rate-limits sign-in under this
/// suite's login volume, the same way its email-sending endpoints already did (see
/// FakeSupabaseAuthGateway's other faked methods).
///
/// <see cref="SigningKey"/> is NOT a real secret — it protects nothing production-relevant. It only
/// needs to be internally self-consistent between this E2E-only token issuer and the E2E-only
/// validation candidate wired into HR.Api's JWT bearer options (Program.cs's
/// ConfigureSupabaseJwtBearer), both gated by the exact same E2E_TESTING=true check. It is never
/// reachable, issued, or accepted unless that flag is set — the app never sets it outside a local/CI
/// E2E test run (see HR.AppHost/AppHost.cs and tests/HR.Web.E2E.Tests/Infrastructure/AppFixture.cs).
/// The real Supabase JWKS validation path in Program.cs is completely unchanged for every other
/// environment.
/// </summary>
internal static class E2eFakeSupabaseJwt
{
    // Fixed, hardcoded, non-secret string — deliberately not read from configuration/user-secrets,
    // since it isn't protecting anything. Only its self-consistency between issuance (here) and
    // validation (HR.Api's Program.cs) matters.
    private static readonly byte[] KeyBytes =
        Encoding.UTF8.GetBytes("e2e-testing-only-fake-jwt-key-not-a-real-secret-32-bytes-min!!");

    public static SecurityKey SigningKey { get; } = new SymmetricSecurityKey(KeyBytes);

    /// <summary>
    /// Mints a locally-signed access token shaped to satisfy HR.Api's real Supabase
    /// TokenValidationParameters (issuer/audience/lifetime) and the claims HR.Modules.Identity reads
    /// literally off the principal (CurrentUserClaims.SupabaseUserId = "sub", .Email = "email") —
    /// see SupabaseCurrentUserResolutionMiddleware / HttpContextCurrentUser.
    /// </summary>
    public static string CreateAccessToken(string supabaseProjectUrl, Guid userId, string email, TimeSpan lifetime)
    {
        var handler = new JwtSecurityTokenHandler();
        var now = DateTime.UtcNow;

        var token = new JwtSecurityToken(
            issuer: $"{supabaseProjectUrl}/auth/v1",
            audience: "authenticated",
            claims:
            [
                new Claim("sub", userId.ToString()),
                new Claim("email", email),
            ],
            notBefore: now,
            expires: now.Add(lifetime),
            signingCredentials: new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256));

        return handler.WriteToken(token);
    }
}
