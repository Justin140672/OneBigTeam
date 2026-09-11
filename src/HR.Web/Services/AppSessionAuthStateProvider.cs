using System.Net.Http.Json;
using System.Security.Claims;
using HR.Web.Models;
using Microsoft.AspNetCore.Components.Authorization;

namespace HR.Web.Services;

// Implements IHostEnvironmentAuthenticationStateProvider so that ASP.NET Core's Blazor Server
// CircuitHost can bridge each SignalR HttpContext.User into this circuit's own (Scoped)
// CircuitSessionState — see CircuitSessionState remarks for why a Scoped DI object, not the
// request-scope instance Program.cs's middleware populated, is required.
//
// IMPORTANT (Ticket 11 correction): earlier comments here claimed CircuitHost calls
// SetAuthenticationState exactly once, at circuit creation. That is FALSE. Per
// ComponentHub.cs (see https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/Components/Server/src/ComponentHub.cs#L211),
// Blazor Server also invokes SetAuthenticationState on every RECONNECT — i.e. whenever the
// browser's persistent SignalR connection drops and re-establishes against the *same* still-alive
// circuit (network blip, tab backgrounding, etc.), using the ClaimsPrincipal from the NEW
// reconnecting request's HttpContext.User. That reconnecting request may carry a different session
// state than the one that created the circuit — e.g. the user logged out or the session cookie
// expired in between, so NoOpAuthenticationHandler now authenticates it as anonymous (or without
// the token claim). The original implementation only ever SET the token when the claim was
// present, and silently no-op'd otherwise — so a circuit that reconnected without its auth cookie
// kept using its OLD retained token for every outgoing API call, directly violating the "a missing
// session must not restore earlier credentials" requirement. Every invocation of this method must
// therefore be treated as authoritative for the circuit's CURRENT auth state, not merely as an
// opportunistic one-time seed.
//
// This never touches the browser: the claim lives only on an in-memory ClaimsPrincipal built by an
// AuthenticationHandler per-request (no cookie/SignInAsync involved), and nothing here is ever
// exposed as a component [Parameter] or serialized page state.
public sealed class AppSessionAuthStateProvider(HrApiHttpClientFactory httpClientFactory, CircuitSessionState sessionState)
    : AuthenticationStateProvider, IHostEnvironmentAuthenticationStateProvider
{
    private static readonly AuthenticationState Anonymous =
        new(new ClaimsPrincipal(new ClaimsIdentity()));

    // Called by the Blazor Server framework at circuit creation AND on every reconnect (see class
    // remarks above) with the ClaimsPrincipal from the connecting request's HttpContext.User.
    public void SetAuthenticationState(Task<AuthenticationState> authenticationStateTask)
    {
        if (authenticationStateTask.IsCompletedSuccessfully)
        {
            ApplyState(authenticationStateTask.Result);
            return;
        }

        // Framework-provided tasks are documented/observed to already be completed (the connecting
        // request's HttpContext.User is available synchronously) — this is a defensive fallback
        // only, executed synchronously on completion so it still runs before the circuit's first
        // render if the task happens to still be pending.
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
    // reconnect — see class remarks). Three cases:
    //
    //   1. No token claim present (anonymous connection, or authenticated-but-missing-claim, which
    //      NoOpAuthenticationHandler never actually produces but is handled defensively anyway) ->
    //      CLEAR the session state. A reconnect without a valid session cookie must not leave an
    //      earlier circuit's token usable for outgoing API calls.
    //
    //   2. Token claim present and IDENTICAL to what this circuit already holds -> no-op. This is
    //      the ordinary "same user reconnects, same still-valid cookie" path; nothing changed, so
    //      nothing should be cleared or re-notified.
    //
    //   3. Token claim present but DIFFERENT from what this circuit already holds (including the
    //      very first time, when the circuit holds nothing yet) -> POLICY DECISION for Ticket 11:
    //      when the circuit had NO prior token (first seed at circuit creation), accept the new
    //      token normally. When the circuit ALREADY had a different token (a live circuit
    //      reconnecting with a materially different session — e.g. a different user's cookie
    //      arriving on a resumed circuit, since NoOpAuthenticationHandler's token hash claim is
    //      derived 1:1 from the raw access token), we deliberately do NOT "hot swap" the identity
    //      of a live circuit that may already hold loaded component state/data for the previous
    //      identity. Building a mechanism to notify every component to re-fetch data for a new
    //      user is exactly the kind of clever/implicit machinery this project avoids (see prior
    //      fix history). Instead we fail closed here too: clear the session state and rely on
    //      Ticket 9's existing "any auth change forces a full navigation/fresh circuit" flow to
    //      give the browser a brand-new circuit with the new identity. The user is left logged out
    //      of the stale circuit rather than silently continuing as a mixed-identity session.
    //
    // Ticket 12 correction: "isFirstSeed" was previously inferred from `sessionState.AccessToken is
    // null`, which is ALSO true right after Clear() invalidates an already-used circuit (case 1/3
    // above). That made the very next call on the SAME invalidated circuit indistinguishable from a
    // brand-new circuit, so a subsequent token (even a different user's) was wrongly accepted —
    // A → B → B and A → anonymous → B both slipped through. We now use CircuitSessionState's
    // explicit Status instead of inferring lifecycle from the nullable token: only a circuit whose
    // Status is still Uninitialized may accept a first token. A circuit whose Status is Invalidated
    // is sticky-blocked — repeated callbacks can never undo the invalidation; only a genuinely new
    // circuit (a fresh CircuitSessionState instance via DI scoping) can authenticate again.
    private void ApplyState(AuthenticationState state)
    {
        var token = state.User.FindFirst(NoOpAuthenticationHandler.SupabaseAccessTokenClaimType)?.Value;

        if (string.IsNullOrEmpty(token))
        {
            sessionState.Clear();
            NotifyAuthenticationStateChanged(Task.FromResult(Anonymous));
            return;
        }

        // Sticky fail-closed: once this circuit has been invalidated, no later callback — not even
        // one carrying a legitimately new token — may revive it.
        if (sessionState.Status == CircuitAuthStatus.Invalidated)
        {
            return;
        }

        if (token == sessionState.AccessToken)
        {
            // Same-user (same-token) reconnect: state is already correct, nothing to do.
            return;
        }

        var isFirstSeed = sessionState.Status == CircuitAuthStatus.Uninitialized;
        if (isFirstSeed)
        {
            sessionState.SetToken(token);
            NotifyAuthenticationStateChanged(Task.FromResult(state));
            return;
        }

        // A live, already-Authenticated circuit just received a different token — treat as a
        // different-identity reconnect and fail closed (see policy note above). Clear() transitions
        // this circuit's Status to Invalidated, so it cannot be revived by any later callback.
        sessionState.Clear();
        NotifyAuthenticationStateChanged(Task.FromResult(Anonymous));
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            var http     = httpClientFactory.CreateClient();
            var response = await http.GetAsync("api/me");

            if (!response.IsSuccessStatusCode)
                return Anonymous;

            var me = await response.Content.ReadFromJsonAsync<MeResponse>();
            if (me is null)
                return Anonymous;

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, me.UserId.ToString()),
                new Claim(ClaimTypes.Email,          me.Email ?? string.Empty),
                new Claim("company_id",              me.CompanyId.ToString()),
            };

            var identity = new ClaimsIdentity(claims, authenticationType: "hrapi");
            return new AuthenticationState(new ClaimsPrincipal(identity));
        }
        catch
        {
            return Anonymous;
        }
    }
}
