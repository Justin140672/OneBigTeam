using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;

namespace HR.Admin.Web.Services;

// Drives Blazor's own AuthorizeRouteView/AuthorizeView. Determines *authentication* only (does a
// valid Supabase session exist at all) — never fine-grained authorisation, which every
// individual page/service already enforces separately via its own API call and null-means-"show
// error banner" contract (see e.g. CustomerDetailsService.GetCustomerDetailsOrNullAsync).
//
// This used to call the tenant-oriented /api/me (HR.Modules.Identity's GetMe feature), the same
// endpoint HR.Web's own AppSessionAuthStateProvider uses. That endpoint requires "role:employee"
// and unconditionally resolves a TenantId/company, so a platform-administrator-only account (no
// UserRole/Employee/tenant at all — e.g. justinetherington@hotmail.com, seeded purely via
// PlatformAdmin:AllowedEmails / identity.platform_administrators) got a 403 and was treated as
// *not authenticated at all* by Blazor's router, bouncing them to /login despite having a
// perfectly valid platform-administrator session.
//
// Now calls HR.Modules.Identity's GetPlatformAdminMe feature (GET /api/platform-admin/me),
// gated on the "platform:admin" policy instead — the same DB-backed policy already used by
// ~30 other Admin-facing endpoints (see PlatformAdminAuthorizationHandler). It does not resolve
// any tenant/company, so it succeeds for platform-admin-only accounts as well as tenant users who
// also happen to be platform administrators. The Admin Portal is platform-admin-only by design,
// so this is the correct authentication probe for every page in this app.
public sealed class AppSessionAuthStateProvider(
    IHttpClientFactory httpClientFactory, ILogger<AppSessionAuthStateProvider> logger)
    : AuthenticationStateProvider
{
    private static readonly AuthenticationState Anonymous =
        new(new ClaimsPrincipal(new ClaimsIdentity()));

    private static AuthenticationState Authenticated(string name) =>
        new(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, name)], authenticationType: "hrapi")));

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        // Bounded so a slow/unreachable API can't hang navigation on every page (including
        // /login) indefinitely — degrades to "not signed in" instead; real enforcement of what an
        // authenticated user can actually see still happens server-side on every real API call.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            var http = httpClientFactory.CreateClient("hrapi");
            var response = await http.GetAsync("api/platform-admin/me", cts.Token);

            // The Admin Portal is platform-admin-only, and Login.razor already rejects a
            // valid-but-not-allow-listed sign-in on the login page (so a non-admin never gets a
            // usable cookie). Anything short of a 200 here therefore means "not a usable Admin
            // Portal session" — treat it as anonymous and let the router send them to /login.
            if (!response.IsSuccessStatusCode)
                return Anonymous;

            var me = await response.Content.ReadFromJsonAsync<MeResponse>(cancellationToken: cts.Token);
            return Authenticated(me?.Email ?? "platform-admin");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to resolve authentication state via /api/platform-admin/me");
            return Anonymous;
        }
    }

    // Minimal projection of HR.Modules.Identity.Features.GetPlatformAdminMe.GetPlatformAdminMeResponse
    // — only the fields this probe actually needs (proving the call succeeded, plus a display name
    // for the claim). Role is intentionally not projected here; no Admin.Web page currently reads it
    // from auth state.
    private sealed record MeResponse(Guid UserId, string? Email);
}
