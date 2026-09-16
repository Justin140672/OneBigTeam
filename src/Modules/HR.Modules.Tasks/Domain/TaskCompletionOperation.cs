using HR.SharedKernel;

namespace HR.Modules.Tasks.Domain;

/// <summary>
/// Ticket 4 (P1): a durable, observable record of a task-completion request, persisted alongside
/// the <see cref="TaskItem"/> transition so a crash/failure partway through completion's side
/// effects (notification, audit, downstream business dispatch) never leaves a completed task with
/// silently-lost decision data or a permanently-stuck notification/audit step.
///
/// Lifecycle:
///  - Pending: request received, decision payload persisted, business dispatch not yet attempted.
///  - Rejected: the business dispatch (see ITaskCompletionAction/TaskCompletionDispatcher) rejected
///    the decision — terminal, not retried; the underlying TaskItem was never marked Completed.
///  - DispatchApplied: the business dispatch succeeded and the TaskItem was marked Completed, but
///    the remaining side effects (notification write, audit publish) have not yet been confirmed —
///    either still in-flight or handed to <c>TaskCompletionEffectsJob</c> for retry.
///  - Processed: fully applied — business dispatch succeeded AND every side effect confirmed.
///
/// Ticket 19 (P2): implements <see cref="IVersionedAggregate"/> — same claim/lease idiom as
/// <see cref="HR.Modules.Identity.Domain.AccountDisablement"/> and
/// <see cref="HR.Modules.Recruitment.Domain.CandidateDocumentDeletionOperation"/>. Previously
/// <see cref="Jobs.TaskCompletionReconciliationJob"/> substituted a fixed "no progress within 5
/// minutes" age check (on <c>CreatedAt</c>/<c>LastAttemptAt</c>) for any real dispatch lease, with
/// no concurrency token guarding the replay/re-enqueue decision at all — a live HTTP dispatch
/// completing the SAME operation while a reconciliation sweep concurrently decided it looked
/// abandoned could race it. <see cref="Claim"/> is only ever persisted via
/// <see cref="DbContextConcurrencyExtensions.SaveChangesWithConcurrencyAsync{T}"/>, so exactly one
/// caller's claim for a given row can ever win.
/// </summary>
internal sealed class TaskCompletionOperation : IVersionedAggregate
{
    private TaskCompletionOperation() { }

    public const string StatusPending         = "pending";
    public const string StatusRejected        = "rejected";
    public const string StatusDispatchApplied = "dispatch_applied";
    public const string StatusProcessed       = "processed";

    /// <summary>Ticket 19 (P2): same generous-relative-to-normal-runtime lease duration as
    /// AccountDisablement.LeaseDuration — see that type's remarks for the reasoning. Both
    /// CompleteTaskHandler's own live dispatch and TaskCompletionEffectsJob normally complete in
    /// well under a second.</summary>
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(10);

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid TaskId { get; private set; }
    public Guid CompletedBy { get; private set; }
    public string? OutcomeDecision { get; private set; }
    public string? OutcomeReason { get; private set; }
    public string Status { get; private set; } = StatusPending;
    public int AttemptCount { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }

    // Ticket 19 (P2): explicit, persisted optimistic-concurrency token — see AssetCategory.Version
    // for the same established idiom.
    public int Version { get; private set; } = 1;

    public Guid? ClaimedBy { get; private set; }
    public DateTimeOffset? LeaseExpiresAt { get; private set; }

    public void IncrementVersion() => Version++;

    public static TaskCompletionOperation CreatePending(
        Guid id,
        Guid companyId,
        Guid taskId,
        Guid completedBy,
        string? outcomeDecision,
        string? outcomeReason,
        DateTimeOffset now)
    {
        return new TaskCompletionOperation
        {
            Id = id,
            CompanyId = companyId,
            TaskId = taskId,
            CompletedBy = completedBy,
            OutcomeDecision = outcomeDecision,
            OutcomeReason = outcomeReason,
            Status = StatusPending,
            AttemptCount = 0,
            CreatedAt = now,
            Version = 1,
        };
    }

    /// <summary>
    /// Ticket 19 (P2): claims this row for <paramref name="workerId"/> for
    /// <see cref="LeaseDuration"/> — callers must persist this via
    /// <see cref="DbContextConcurrencyExtensions.SaveChangesWithConcurrencyAsync{T}"/> with the
    /// version this instance was loaded at. Does NOT change <see cref="Status"/> — this operation's
    /// status transitions (Pending -&gt; Rejected/DispatchApplied -&gt; Processed) are driven
    /// separately by the business outcome, not by claim state; a claim only ever gates WHO is
    /// currently allowed to attempt the next step.
    /// </summary>
    public void Claim(Guid workerId, DateTimeOffset now)
    {
        ClaimedBy = workerId;
        LeaseExpiresAt = now + LeaseDuration;
        AttemptCount++;
        LastAttemptAt = now;
    }

    /// <summary>True when <paramref name="workerId"/> is the current, non-expired claim holder.</summary>
    public bool IsClaimedBy(Guid workerId, DateTimeOffset now) =>
        ClaimedBy == workerId && LeaseExpiresAt is { } expiresAt && expiresAt > now;

    /// <summary>Releases the claim once the claimed work has been completed (successfully or not) —
    /// leaves the row claimable again immediately rather than waiting out the rest of the lease.</summary>
    public void ReleaseClaim()
    {
        ClaimedBy = null;
        LeaseExpiresAt = null;
    }

    public void MarkRejected(string reason, DateTimeOffset now)
    {
        Status = StatusRejected;
        FailureReason = reason;
        LastAttemptAt = now;
        ReleaseClaim();
    }

    public void MarkDispatchApplied(DateTimeOffset now)
    {
        // Ticket 19 (P2): deliberately does NOT release the claim — DispatchApplied is itself one
        // of TaskCompletionReconciliationJob's "abandoned" query targets (rows whose side effects
        // were never confirmed). Releasing it here would make a row that was JUST claimed and
        // transitioned re-eligible for "abandoned" re-enqueue within the SAME sweep (the caller
        // typically enqueues TaskCompletionEffectsJob immediately after this call anyway). The
        // claim/lease is released only once side effects are actually confirmed (MarkProcessed) or
        // its lease naturally expires.
        Status = StatusDispatchApplied;
        LastAttemptAt = now;
    }

    public void RecordAttempt(DateTimeOffset now)
    {
        AttemptCount++;
        LastAttemptAt = now;
    }

    public void MarkProcessed(DateTimeOffset now)
    {
        Status = StatusProcessed;
        ProcessedAt = now;
        FailureReason = null;
        ReleaseClaim();
    }

    public void RecordFailure(string reason)
    {
        FailureReason = reason;
    }
}
