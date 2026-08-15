using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HR.Admin.Web.Services;

// Mirrors HR.Web.Services.NoOpAuthenticationHandler — see that file's remarks. This only checks
// whether an Admin Portal session cookie exists so SSR-level [Authorize] on Razor Component pages
// agrees with AppSessionAuthStateProvider below; it does not validate the token itself. The real
// security boundary is HR.Api's "platform:admin" endpoint policy plus the
// PlatformAdmin:AllowedEmails allow-list enforced server-side in GetCustomerDashboardHandler.
public sealed class NoOpAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var accessToken = Context.Request.Cookies[SupabaseSessionAccessor.CookieName];
        if (string.IsNullOrEmpty(accessToken))
            return Task.FromResult(AuthenticateResult.NoResult());

        var tokenHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(accessToken)));
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, tokenHash)], authenticationType: Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Redirect("/login");
        return Task.CompletedTask;
    }
}
