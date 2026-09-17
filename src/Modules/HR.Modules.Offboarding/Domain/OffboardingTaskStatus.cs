namespace HR.Modules.Offboarding.Domain;

internal enum OffboardingTaskStatus
{
    Pending = 1,
    InProgress = 2,
    Completed = 3,

    // Legacy/historical value. Prior to the Waived/Cancelled split, every non-completed terminal
    // outcome (HR waiver, backdating auto-resolution, cancellation cascade, or a genuine unresolved
    // historical skip) was stored as Skipped. New code paths must use Waived or Cancelled instead —
    // Skipped is retained only so historical rows without a reconciled actor/reason continue to
    // deserialize and can be surfaced as an unresolved/flagged item (see spec SPEC-OFF-01, Offboarding
    // obligation state model).
    Skipped = 4,

    // An authorised HR user (or the system, for a backdating-driven auto-resolution) decided the
    // obligation is not required. Always carries a mandatory reason and actor (SkipReason/
    // SkippedByUserId/SkippedAt are reused as the waiver reason/actor/timestamp). Terminal. Counts as
    // "resolved" but must never be labelled Completed.
    Waived = 5,

    // The parent leaving process was cancelled, so this obligation is cancelled with it. Terminal.
    // Never counted as completed or waived, and never contributes to plan completion/progress.
    Cancelled = 6
}
