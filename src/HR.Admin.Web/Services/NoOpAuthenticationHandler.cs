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
    // Server-side-only claim type carrying the raw Supabase access token so it can be bridged into a
    // Blazor Server circuit's own DI scope via IHostEnvironmentAuthenticationStateProvider — see the
    // remarks below and on AppSessionAuthStateProvider.SetAuthenticationState. Mirrors
    // HR.Web.Services.NoOpAuthenticationHandler.
    public const string SupabaseAccessTokenClaimType = "obt:supabase_at";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var accessToken = Context.Request.Cookies[SupabaseSessionAccessor.CookieName];
        if (string.IsNullOrEmpty(accessToken))
            return Task.FromResult(AuthenticateResult.NoResult());

        var tokenHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(accessToken)));

        // Security ticket follow-up (circuit-scope token bridging): Blazor Server creates a
        // SEPARATE DI scope per interactive circuit, so the Scoped CircuitSessionState populated by
        // Program.cs's request middleware (resolved from the negotiating HTTP request's OWN scope)
        // is never the circuit's own instance — the circuit's CircuitSessionState starts empty.
        // ASP.NET Core's supported bridge is IHostEnvironmentAuthenticationStateProvider
        // .SetAuthenticationState, which CircuitHost calls once, at circuit creation, with the
        // ClaimsPrincipal from the connecting SignalR request's HttpContext.User — the same
        // HttpContext this handler authenticates on every request (including that connect). This
        // claim never leaves the server (no SignInAsync/cookie persistence; built fresh per request
        // by this AuthenticationHandler), so it is never serialized to the browser, never a
        // component [Parameter], and never client-visible.
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, tokenHash),
                new Claim(SupabaseAccessTokenClaimType, accessToken),
            ],
            authenticationType: Scheme.Name);
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
