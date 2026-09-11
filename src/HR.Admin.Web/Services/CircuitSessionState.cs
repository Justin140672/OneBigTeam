namespace HR.Admin.Web.Services;

// Mirrors HR.Web.Services.CircuitSessionState — see that file's remarks for the full rationale
// (Scoped DI, not AsyncLocal, is required because Blazor Server dispatches each interactive circuit
// event as its own fresh logical call context that AsyncLocal does not flow into). Deliberately
// duplicated rather than shared: HR.Web and HR.Admin.Web are separate deployable apps and this class
// carries no business logic.
// Ticket 12: explicit circuit lifecycle state — see HR.Web's CircuitSessionState for the full
// rationale (mirrored exactly here; deliberately duplicated, not shared, per this class's existing
// convention).
public enum CircuitAuthStatus
{
    Uninitialized,
    Authenticated,

    // Sticky: once Invalidated, this instance must never accept another token — only a genuinely
    // new circuit (a brand-new CircuitSessionState instance via DI scoping) can authenticate again.
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

    public void Clear()
    {
        AccessToken = null;
        if (Status == CircuitAuthStatus.Authenticated)
            Status = CircuitAuthStatus.Invalidated;
    }
}
