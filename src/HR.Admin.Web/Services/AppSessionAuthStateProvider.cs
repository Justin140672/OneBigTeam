using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;

namespace HR.Admin.Web.Services;

// Drives Blazor's own AuthorizeRouteView/AuthorizeView. Determines *authentication* only (does a
// valid Supabase session exist at all) — never platform-admin authorisation, which every
// individual page/service already enforces separately via its own API call and null-means-"show
// error banner" contract (see e.g. CustomerDetailsService.GetCustomerDetailsOrNullAsync).
//
// This used to reuse the customer-dashboard call (a business endpoint with its own
// "PlatformAdmin:AllowedEmails" gate baked in) as a combined authentication+authorisation probe.
// That conflated the two: a real, logged-in-but-not-allow-listed persona (e.g. a plain employee)
// got treated as *not authenticated at all* by Blazor's router, which redirected them to /login
// instead of letting CustomerDetails.razor/FailedPayments.razor render their own intended "you're
// authenticated but not authorised" banners. It also meant any bug/slow query/transient failure
// on the dashboard endpoint didn't just break the dashboard — it hung or crashed navigation on
// every single page in the app, including /login itself (AuthorizeRouteView awaits this before
// evaluating even an [AllowAnonymous] page's own policy).
//
// Calls the same lightweight /api/me (HR.Modules.Identity's GetMe feature) HR.Web's own
// AppSessionAuthStateProvider uses — a pure "who is the current user" check requiring only
// "role:employee" and a resolved company, nothing platform-admin-specific. Every seeded persona
// used by this portal's E2E tests carries SystemRoles.Employee alongside their specific role, so
// this correctly succeeds for any real logged-in user regardless of platform-admin allow-list
// status.
public sealed class AppSessionAuthStateProvider(
    IHttpClientFactory httpClientFactory, ILogger<AppSessionAuthStateProvider> logger)
    : AuthenticationStateProvider
{
    private static readonly AuthenticationState Anonymous =
        new(new ClaimsPrincipal(new ClaimsIdentity()));

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        // Bounded so a slow/unreachable API can't hang navigation on every page (including
        // /login) indefinitely — degrades to "not signed in" instead; real enforcement of what an
        // authenticated user can actually see still happens server-side on every real API call.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            var http = httpClientFactory.CreateClient("hrapi");
            var response = await http.GetAsync("api/me", cts.Token);

            if (!response.IsSuccessStatusCode)
                return Anonymous;

            var me = await response.Content.ReadFromJsonAsync<MeResponse>(cancellationToken: cts.Token);
            if (me is null)
                return Anonymous;

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, me.Email ?? "platform-admin")], authenticationType: "hrapi");
            return new AuthenticationState(new ClaimsPrincipal(identity));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to resolve authentication state via /api/me");
            return Anonymous;
        }
    }

    // Minimal projection of HR.Modules.Identity.Features.GetMe.GetMeResponse — only the fields
    // this probe actually needs (proving the call succeeded, plus a display name for the claim).
    private sealed record MeResponse(Guid UserId, string? Email);
}
