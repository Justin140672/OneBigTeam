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
    private readonly DevPersonaStore _personaStore;

    public DevAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        DevPersonaStore personaStore)
        : base(options, logger, encoder)
    {
        _personaStore = personaStore;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var persona = _personaStore.Current;

        var claims = new[]
        {
            new Claim("sub",                          persona.UserId),
            new Claim(ClaimTypes.NameIdentifier,      persona.UserId),
            new Claim("email",                        persona.Email),
            new Claim("company_id",                   persona.CompanyId),
        };

        var identity  = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket    = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
