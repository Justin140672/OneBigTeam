namespace HR.SharedKernel.Idempotency;

/// <summary>
/// Ticket 3 (P1) follow-up items 1/2: owns the lifecycle of one client-generated idempotency key for
/// one logical user operation (e.g. "submit this leave adjustment", "create this asset").
///
/// A caller reads <see cref="Current"/> to get the key for THIS attempt - the same key comes back on
/// every read until <see cref="Reset"/> is called, so a lost response, a manual "Try again", a
/// component rerender while the call is still pending, or a double-click submit all reuse the same
/// key and dedupe server-side. Call <see cref="Reset"/> once the operation reaches a definitive
/// outcome (success, or a validation/business failure the user must correct and resubmit as a new
/// action) so the NEXT submission gets a fresh key, even if its payload happens to be identical.
///
/// Typically held as a field on the Blazor component or scoped service that owns one such operation -
/// component fields persist across re-renders of the same component instance, which is exactly the
/// "rerender while pending" case this needs to survive.
/// </summary>
public sealed class IdempotencyKeyScope
{
    private Guid? _key;

    /// <summary>The key for the in-progress (or about-to-start) attempt of this logical operation.</summary>
    public Guid Current => _key ??= Guid.NewGuid();

    /// <summary>Call after a definitive outcome so the next call starts a genuinely new operation.</summary>
    public void Reset() => _key = null;
}
