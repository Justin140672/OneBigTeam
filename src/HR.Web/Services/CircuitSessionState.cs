namespace HR.Web.Services;

// The single source of truth for "what Supabase access token belongs to this circuit/request".
// Registered Scoped, so Blazor Server gives each interactive circuit its own instance for the whole
// lifetime of that circuit — exactly like every other per-circuit service in this app (AppSession,
// EmployeeService, etc.), all of which already resolve correctly per-circuit because Blazor Server
// creates one DI scope per circuit and components/services are resolved from it directly.
//
// This replaces the old AsyncLocal<string?> ambient-token trick. AsyncLocal only flows down the
// *logical* async call tree of a single top-level operation. Blazor Server's interactive circuit
// dispatches each browser event (button click, etc.) as its own top-level SignalR Hub invocation,
// which starts a *fresh* logical call context — it is not nested inside the ExecutionContext of the
// HTTP request that originally established the circuit. So an AsyncLocal value set during the
// circuit's initial HTTP request/prerender is invisible to later interactive event handlers, even
// though they run on the very same circuit for the very same user. A Scoped DI object does not have
// this problem: Blazor Server resolves it from the circuit's own scope every time, regardless of
// which physical thread or ExecutionContext is currently running.
//
// Deliberately NOT injected into SupabaseAuthDelegatingHandler-style pooled HttpMessageHandlers.
// IHttpClientFactory resolves handler pipeline dependencies from its own internal scope tied to the
// named client's HandlerLifetime, not the caller's scope — a classic captive dependency, and the
// original root cause of the P1 cross-user token leak this type replaces. Instead, HrApiHttpClientFactory
// (also Scoped) reads this value directly, in the actual caller's scope, and attaches it to the
// HttpClient before handing it back — no pooled handler is ever involved in carrying user identity.
// Ticket 12: explicit circuit lifecycle state, replacing the earlier (buggy) inference of
// "is this circuit new" from `AccessToken is null`. That inference broke because Clear() also
// produces a null AccessToken when an ALREADY-USED circuit is invalidated (anonymous reconnect, or
// a different identity arriving on a live circuit) — so the very next SetAuthenticationState call
// on that SAME invalidated circuit wrongly looked identical to a brand-new, never-seeded circuit
// and happily accepted a new (possibly different-user) token. See
// AppSessionAuthStateProvider.ApplyState for the consumer of this state.
public enum CircuitAuthStatus
{
    // Never seeded with any real token yet (fresh circuit, or a circuit that has only ever seen
    // anonymous connections). A first authenticated token must still be accepted normally.
    Uninitialized,

    // Currently holds a valid token for its current identity.
    Authenticated,

    // Was authenticated and was then invalidated (anonymous reconnect, or a different identity
    // arriving on a live circuit). Sticky: once Invalidated, this instance must never accept
    // another token — only a genuinely new circuit (a brand-new CircuitSessionState instance via
    // DI scoping) can authenticate again.
    Invalidated,
}

public sealed class CircuitSessionState
{
    public string? AccessToken { get; private set; }

    public CircuitAuthStatus Status { get; private set; } = CircuitAuthStatus.Uninitialized;

    public void SetToken(string? accessToken)
    {
        AccessToken = accessToken;
        Status = CircuitAuthStatus.Authenticated;
    }

    // Called from the logout path, and from AppSessionAuthStateProvider whenever a
    // SetAuthenticationState call must fail closed (no token claim present, or a different token
    // arriving on an already-Authenticated circuit). Once this runs, no later call on this
    // circuit/request scope can resume sending the old bearer token, even if a race means some
    // in-flight code path never sees a live HttpContext (e.g. a background circuit continuation) —
    // the scoped store itself is now authoritative-empty rather than possibly-stale.
    //
    // Deliberately does NOT reset Status back to Uninitialized: a circuit that was already
    // Authenticated and is now being cleared must transition to Invalidated (sticky — see enum
    // remarks), not revert to looking like a never-used circuit. A circuit that was still
    // Uninitialized (e.g. an anonymous visitor on the login page, never yet authenticated) simply
    // stays Uninitialized so its first real login still works normally. A circuit that was already
    // Invalidated stays Invalidated.
    public void Clear()
    {
        AccessToken = null;
        if (Status == CircuitAuthStatus.Authenticated)
            Status = CircuitAuthStatus.Invalidated;
    }
}
