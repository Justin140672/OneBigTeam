using System.Collections.Concurrent;
using HR.SharedKernel;

namespace HR.Modules.Identity.Authorization;

/// <summary>
/// IAM-08: audit-volume control for permission-denial auditing. A single misconfigured UI element
/// (e.g. a nav item a user can see but not open) can otherwise generate one denial per click/poll,
/// drowning the audit trail in routine, harmless noise. This throttle keeps exactly one audit entry
/// per (user, permission) pair per <see cref="Window"/>, plus a single escalated "repeated denial"
/// entry the moment a burst crosses <see cref="RepeatedDenialThreshold"/> within that same window —
/// the escalated entry is the security-relevant signal (the same denial happening far more than a
/// normal UI mis-click would produce), while everything in between is deliberately dropped rather
/// than logged.
///
/// Deliberately an in-process, module-local, singleton-scoped component (not a SharedKernel/
/// Infrastructure abstraction) — there was no existing rate-limiting/deduplication utility
/// elsewhere in the codebase to reuse (checked HR.SharedKernel and HR.Infrastructure), and this
/// concern is specific to the authorization hot path inside Identity, not a cross-cutting service
/// other modules need. Best-effort: process-local counters reset on restart/across instances,
/// which is acceptable for volume control (as opposed to a correctness-critical security control).
/// </summary>
internal sealed class PermissionDenialAuditThrottle(IClock clock)
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    private const int RepeatedDenialThreshold = 5;

    private readonly ConcurrentDictionary<(Guid UserId, Guid PermissionId), DenialWindowState> _state = new();

    /// <summary>
    /// Records a denial and returns whether it should be audited. Returns true exactly once per
    /// window (the first denial) and once more if/when the burst reaches
    /// <see cref="RepeatedDenialThreshold"/> denials inside that same window (the escalation entry,
    /// <paramref name="isRepeatedEscalation"/> = true) — every other denial in between is suppressed.
    /// </summary>
    public bool ShouldAudit(Guid userId, Guid permissionId, out bool isRepeatedEscalation, out int denialCountInWindow)
    {
        var now = clock.UtcNowOffset();
        var key = (userId, permissionId);

        var state = _state.AddOrUpdate(
            key,
            _ => new DenialWindowState(now, 1, EscalatedInWindow: false),
            (_, existing) =>
            {
                if (now - existing.WindowStart > Window)
                    return new DenialWindowState(now, 1, EscalatedInWindow: false);

                return existing with { Count = existing.Count + 1 };
            });

        denialCountInWindow = state.Count;

        if (state.Count == 1)
        {
            isRepeatedEscalation = false;
            return true;
        }

        if (state.Count >= RepeatedDenialThreshold && !state.EscalatedInWindow)
        {
            // Mark escalated so we don't re-audit every subsequent denial in the same window.
            _state.TryUpdate(key, state with { EscalatedInWindow = true }, state);
            isRepeatedEscalation = true;
            return true;
        }

        isRepeatedEscalation = false;
        return false;
    }

    private sealed record DenialWindowState(DateTimeOffset WindowStart, int Count, bool EscalatedInWindow);
}
