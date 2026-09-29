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
    // Bug fix (P1 follow-up to Ticket 3): production callers commonly pass C# value tuples as the
    // snapshot (e.g. `(CompanyId, EmployeeId, request)`) - value tuples expose their contents as
    // PUBLIC FIELDS, not properties. Default System.Text.Json options only serialize properties, so
    // every tuple snapshot silently fingerprinted to "{}" and a changed request never rotated the
    // key. IncludeFields makes tuple (and any other field-based) snapshots serialize their real
    // contents so a material change is actually detected.
    private static readonly JsonSerializerOptions SnapshotOptions = new() { IncludeFields = true };

    private readonly IdempotencyKeyScope _key = new();
    private string? _pendingFingerprint;

    public Guid PrepareKey(object requestSnapshot)
    {
        var fingerprint = JsonSerializer.Serialize(requestSnapshot, SnapshotOptions);
        if (_pendingFingerprint != fingerprint)
        {
            _key.Reset();
            _pendingFingerprint = fingerprint;
        }

        return _key.Current;
    }

    public void Complete()
    {
        _key.Reset();
        _pendingFingerprint = null;
    }
}
