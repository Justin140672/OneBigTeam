using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HR.Integration.Tests.Infrastructure;

internal sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "TestAuth";
    public const string UserHeader = "X-Test-User";
    public const string TenantHeader = "X-Test-Tenant";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var userIdValues) || string.IsNullOrWhiteSpace(userIdValues))
        {
            return Task.FromResult(AuthenticateResult.Fail("No user header present."));
        }

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userIdValues.ToString()),
            new Claim("sub", userIdValues.ToString())
        };

        if (Request.Headers.TryGetValue(TenantHeader, out var tenantIdValues) && !string.IsNullOrWhiteSpace(tenantIdValues))
        {
            claims.Add(new Claim("company_id", tenantIdValues.ToString()));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
