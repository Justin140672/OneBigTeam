using System.Text.Json;

namespace HR.SharedKernel.Idempotency;

/// <summary>
/// Ticket 3 (P1) final follow-up items 1/2: owns the full "same key on an unchanged retry, new key
/// the moment the request changes, discard on a definitive outcome" lifecycle for one logical
/// operation, built on top of <see cref="IdempotencyKeyScope"/>. Held as a field on whatever
/// represents that one operation (a dialog component, an edit page) - never on a scoped service,
/// which could be serving more than one such operation at once.
/// </summary>
public sealed class PendingIdempotentOperation
{
    private readonly IdempotencyKeyScope _key = new();
    private string? _pendingFingerprint;

    /// <summary>
    /// Returns the key for this attempt. Rotates to a fresh key first if
    /// <paramref name="requestSnapshot"/> (typically a tuple/record of every field that affects the
    /// request) differs from the snapshot passed on the previous call that hasn't yet been
    /// <see cref="Complete"/>d - i.e. the user changed something material since the last (ambiguous)
    /// attempt, so this must be treated as a new logical operation rather than a retry.
    /// </summary>
    public Guid PrepareKey(object requestSnapshot)
    {
        var fingerprint = JsonSerializer.Serialize(requestSnapshot);
        if (_pendingFingerprint != fingerprint)
        {
            _key.Reset();
            _pendingFingerprint = fingerprint;
        }

        return _key.Current;
    }

    /// <summary>
    /// Call once this operation has reached a definitive outcome (success, or a rejection the user
    /// must act on) - never on an ambiguous failure (lost response/timeout), which must leave the
    /// key and snapshot untouched so the caller's retry reuses them.
    /// </summary>
    public void Complete()
    {
        _key.Reset();
        _pendingFingerprint = null;
    }
}
