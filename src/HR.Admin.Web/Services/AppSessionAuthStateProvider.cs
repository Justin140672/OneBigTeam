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
// Also implements IHostEnvironmentAuthenticationStateProvider — see HR.Web's
// AppSessionAuthStateProvider for the full rationale (mirrored exactly here).
//
// IMPORTANT (Ticket 11 correction): earlier comments here claimed CircuitHost calls
// SetAuthenticationState exactly once, at circuit creation. That is FALSE. Per
// ComponentHub.cs (see https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/Components/Server/src/ComponentHub.cs#L211),
// Blazor Server also invokes SetAuthenticationState on every RECONNECT of an existing, still-alive
// circuit, using the ClaimsPrincipal from the NEW reconnecting request's HttpContext.User — which
// may be anonymous (or lack the token claim) if the session cookie was removed/expired since the
// circuit was created (e.g. logout in another tab). The original implementation only ever SET the
// token when the claim was present and silently no-op'd otherwise, so a reconnecting circuit kept
// using its OLD retained token for outgoing API calls — violating the "a missing session must not
// restore earlier credentials" requirement. Every invocation must be treated as authoritative for
// the circuit's CURRENT auth state.
public sealed class AppSessionAuthStateProvider(
    HrApiHttpClientFactory httpClientFactory, ILogger<AppSessionAuthStateProvider> logger, CircuitSessionState sessionState)
    : AuthenticationStateProvider, IHostEnvironmentAuthenticationStateProvider
{
    private static readonly AuthenticationState Anonymous =
        new(new ClaimsPrincipal(new ClaimsIdentity()));

    private static AuthenticationState Authenticated(string name) =>
        new(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, name)], authenticationType: "hrapi")));

    public void SetAuthenticationState(Task<AuthenticationState> authenticationStateTask)
    {
        if (authenticationStateTask.IsCompletedSuccessfully)
        {
            ApplyState(authenticationStateTask.Result);
            return;
        }

        _ = authenticationStateTask.ContinueWith(
            t =>
            {
                if (t.IsCompletedSuccessfully)
                {
                    ApplyState(t.Result);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    // Fail-closed, authoritative handling of every SetAuthenticationState call (initial connect AND
    // reconnect). See HR.Web's AppSessionAuthStateProvider.ApplyState for the full policy
    // rationale, mirrored exactly here:
    //   1. No token claim -> Clear() the session state (fail closed).
    //   2. Token identical to what the circuit already holds -> no-op (same-user reconnect).
    //   3a. No prior token held (first seed at circuit creation) -> accept the new token.
    //   3b. A different token already held (different-identity reconnect on a live circuit) ->
    //       fail closed (Clear()) rather than hot-swapping identity mid-circuit; Ticket 9's
    //       full-navigation re-auth flow gives the browser a fresh circuit for the new identity
    //       instead. Deliberately not attempting a "notify components to re-fetch for new user"
    //       mechanism — simple explicit fail-closed state beats clever implicit machinery.
    //   2a. Ticket 12 correction: "isFirstSeed" was previously inferred from
    //       `sessionState.AccessToken is null`, which is ALSO true right after Clear() invalidates
    //       an already-used circuit. That made the next call on the SAME invalidated circuit
    //       indistinguishable from a brand-new circuit, wrongly accepting a subsequent token (even a
    //       different user's) — A → B → B and A → anonymous → B both slipped through. We now use
    //       CircuitSessionState's explicit Status: only Uninitialized may accept a first token.
    //       Invalidated is sticky-blocked — repeated callbacks can never undo it; only a genuinely
    //       new circuit (a fresh CircuitSessionState instance via DI scoping) can authenticate again.
    private void ApplyState(AuthenticationState state)
    {
        var token = state.User.FindFirst(NoOpAuthenticationHandler.SupabaseAccessTokenClaimType)?.Value;

        if (string.IsNullOrEmpty(token))
        {
            sessionState.Clear();
            NotifyAuthenticationStateChanged(Task.FromResult(Anonymous));
            return;
        }

        // Sticky fail-closed: once invalidated, no later callback may revive this circuit.
        if (sessionState.Status == CircuitAuthStatus.Invalidated)
        {
            return;
        }

        if (token == sessionState.AccessToken)
        {
            return;
        }

        var isFirstSeed = sessionState.Status == CircuitAuthStatus.Uninitialized;
        if (isFirstSeed)
        {
            sessionState.SetToken(token);
            NotifyAuthenticationStateChanged(Task.FromResult(state));
            return;
        }

        sessionState.Clear();
        NotifyAuthenticationStateChanged(Task.FromResult(Anonymous));
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        // Bounded so a slow/unreachable API can't hang navigation on every page (including
        // /login) indefinitely — degrades to "not signed in" instead; real enforcement of what an
        // authenticated user can actually see still happens server-side on every real API call.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            var http = httpClientFactory.CreateClient();
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
