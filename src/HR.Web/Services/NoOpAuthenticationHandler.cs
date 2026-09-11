using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;

namespace HR.Web.Services;

// "NoOp" gates the SSR-level [Authorize] check enforced by RazorComponentsEndpointHandler/
// UseAuthorization() — a *separate* mechanism from AppSessionAuthStateProvider (which drives
// Blazor's own AuthenticationStateProvider/AuthorizeView, and is what actually calls HR.Api's
// /api/me). The two must agree on whether a session exists, or protected pages 302 to /login on
// every fresh navigation regardless of a valid Supabase cookie — this handler previously always
// returned NoResult(), so HttpContext.User.Identity.IsAuthenticated was permanently false and every
// [Authorize] page was permanently unreachable post-login. This doesn't validate the token (HR.Api
// already does that on every real data call via ConfigureSupabaseJwtBearer) — it only checks
// whether a session cookie exists, enough to let SSR-level authorization agree with reality.
public sealed class NoOpAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    // Server-side-only claim type carrying the raw Supabase access token so it can be bridged into a
    // Blazor Server circuit's own DI scope via IHostEnvironmentAuthenticationStateProvider — see the
    // remarks below and on AppSessionAuthStateProvider.SetAuthenticationState.
    public const string SupabaseAccessTokenClaimType = "obt:supabase_at";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var accessToken = Context.Request.Cookies[SupabaseSessionAccessor.CookieName];
        if (string.IsNullOrEmpty(accessToken))
            return Task.FromResult(AuthenticateResult.NoResult());

        // ASP.NET Core's antiforgery system requires every IsAuthenticated=true identity to carry a
        // unique Name claim (it's part of the antiforgery token's additional data, keyed per user) —
        // an identity with no claims at all throws InvalidOperationException from
        // DefaultAntiforgeryTokenGenerator. This handler still isn't validating the token itself
        // (HR.Api does that on every real data call); the hash is just a cheap, stable, unique-enough
        // value derived from the session's own token rather than embedding the raw token as a claim.
        var tokenHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(accessToken)));

        // Security ticket follow-up (circuit-scope token bridging): Blazor Server creates a
        // SEPARATE DI scope per interactive circuit, so the Scoped CircuitSessionState populated by
        // Program.cs's request middleware (which resolves it from the negotiating HTTP request's OWN
        // scope) is never the same instance the circuit later resolves — the circuit's own
        // CircuitSessionState starts empty. ASP.NET Core's own supported bridge for this is
        // IHostEnvironmentAuthenticationStateProvider.SetAuthenticationState, which CircuitHost calls
        // exactly once, at circuit creation, with the ClaimsPrincipal from the *connecting* SignalR
        // request's HttpContext.User — the same HttpContext this handler authenticates on every
        // request (including that connect). Carrying the raw token as a claim here lets
        // AppSessionAuthStateProvider (see its SetAuthenticationState override) extract it and seed
        // that circuit's own CircuitSessionState before any component runs. This claim never leaves
        // the server: it lives only on the in-memory ClaimsPrincipal built per-request by this
        // AuthenticationHandler (no SignInAsync/cookie persistence involved), so it is never
        // serialized back to the browser, never a component [Parameter], and never client-visible —
        // consistent with the "keep credentials out of browser-visible component parameters"
        // requirement.
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
