using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace HR.Api.Authentication;

/// <summary>
/// Development-only authentication handler. Automatically authenticates every
/// request as the seeded admin user so the UI works without a Supabase login
/// flow wired up. Never registered in Production.
/// </summary>
internal sealed class DevAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    // Matches the seeded company from SeedCompaniesAsync
    private static readonly string DevCompanyId = "00000000-0000-0000-0000-000000000001";
    private static readonly string DevUserId = "30000000-0000-0000-0000-000000000001"; // Sarah Chen

    public DevAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim("sub", DevUserId),
            new Claim("email", "admin@dev.local"),
            new Claim("company_id", DevCompanyId),
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
