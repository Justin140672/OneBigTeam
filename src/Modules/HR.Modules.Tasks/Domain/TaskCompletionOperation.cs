using HR.SharedKernel;
using HR.SharedKernel.ExecutionContext;

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
    public const string StatusDataIntegrityFailure = "data_integrity_failure";
    public const string StatusEffectsTerminalFailure = "effects_terminal_failure";
    public const string StatusEffectsVerified = "effects_verified";
    public const string StatusWaived = "waived";

    public const string ResolutionEvidenceRetry = "evidence_retry";
    public const string ResolutionEffectsVerified = "effects_verified";
    public const string ResolutionWaived = "waived";

    public const string CategoryEffectsRetryLimit = "effects_retry_limit";
    public const string CategoryTaskMissing = "task_missing";
    public const string CategoryEvidenceMissing = "evidence_missing";
    public const string CategoryNotificationUnconfirmed = "notification_unconfirmed";
    public const string CategoryAuditUnconfirmed = "audit_unconfirmed";

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
    public string? CommandFingerprint { get; private set; }
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

    // Ticket 23 (P2): durable correlation metadata, stamped from the ambient execution context at
    // creation time - nullable so rows written before this migration remain fully usable.
    public Guid? CorrelationId { get; private set; }
    public Guid? CausationId { get; private set; }
    public Guid? MessageId { get; private set; }

    public DateTimeOffset? TerminalFailureAt { get; private set; }
    public string? FailureCategory { get; private set; }
    public int ResetCount { get; private set; }
    public DateTimeOffset? LastResetAt { get; private set; }
    public Guid? LastResetBy { get; private set; }

    public int AdjudicationCount { get; private set; }
    public string? ResolutionType { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public DateTimeOffset? LastAdjudicatedAt { get; private set; }
    public Guid? LastAdjudicatedBy { get; private set; }

    public DateTimeOffset? SnapshotCapturedAt { get; private set; }
    public bool NotificationRequired { get; private set; }
    public Guid? SnapshotAssignedEmployeeId { get; private set; }
    public string? SnapshotTaskTitle { get; private set; }
    public string? SnapshotTaskDescription { get; private set; }
    public string? PreviousTaskStatus { get; private set; }
    public DateTimeOffset? TaskCompletedAt { get; private set; }

    public bool HasCompletionSnapshot => SnapshotCapturedAt is not null;

    /// <summary>The canonical command for this operation, rebuilt from stored values so legacy rows
    /// persisted un-normalized still dispatch canonical decision/reason.</summary>
    public TaskCompletionCommand ToCommand() =>
        TaskCompletionCommand.Create(TaskId, OutcomeDecision, OutcomeReason);

    public bool IsCommandEquivalentTo(TaskCompletionCommand command) =>
        (CommandFingerprint ?? ToCommand().Fingerprint()) == command.Fingerprint();

    public bool IsTerminalFailure =>
        Status is StatusEffectsTerminalFailure or StatusDataIntegrityFailure;

    public void IncrementVersion() => Version++;

    public void CaptureCompletionSnapshot(
        Guid? assignedEmployeeId,
        string title,
        string? description,
        string previousStatus,
        DateTimeOffset completedAt,
        DateTimeOffset now)
    {
        SnapshotCapturedAt = now;
        NotificationRequired = assignedEmployeeId.HasValue;
        SnapshotAssignedEmployeeId = assignedEmployeeId;
        SnapshotTaskTitle = title.Length > 200 ? title[..200] : title;
        SnapshotTaskDescription = description is { Length: > 500 } ? description[..500] : description;
        PreviousTaskStatus = previousStatus;
        TaskCompletedAt = completedAt;
    }

    public bool IsOperatorResolved => Status is StatusEffectsVerified or StatusWaived;

    public void BeginEvidenceRetry(Guid operatorUserId, DateTimeOffset now)
    {
        Status = StatusDispatchApplied;
        TerminalFailureAt = null;
        FailureCategory = null;
        FailureReason = null;
        AttemptCount = 0;
        ReleaseClaim();
        RecordAdjudication(ResolutionEvidenceRetry, operatorUserId, now);
    }

    public void MarkEffectsVerified(Guid operatorUserId, DateTimeOffset now)
    {
        Status = StatusEffectsVerified;
        ResolvedAt = now;
        ReleaseClaim();
        RecordAdjudication(ResolutionEffectsVerified, operatorUserId, now);
    }

    public void MarkWaived(Guid operatorUserId, DateTimeOffset now)
    {
        Status = StatusWaived;
        ResolvedAt = now;
        ReleaseClaim();
        RecordAdjudication(ResolutionWaived, operatorUserId, now);
    }

    private void RecordAdjudication(string resolutionType, Guid operatorUserId, DateTimeOffset now)
    {
        AdjudicationCount++;
        ResolutionType = resolutionType;
        LastAdjudicatedAt = now;
        LastAdjudicatedBy = operatorUserId;
    }

    public void MarkEffectsTerminalFailure(string category, string reason, DateTimeOffset now)
    {
        Status = StatusEffectsTerminalFailure;
        TerminalFailureAt = now;
        FailureCategory = category;
        FailureReason = reason.Length > 500 ? reason[..500] : reason;
        LastAttemptAt = now;
        ReleaseClaim();
    }

    /// <summary>
    /// Operator recovery: restarts only the outstanding notification/audit effects. The business
    /// dispatch and the task completion are never re-run; confirmed effects are re-verified, not repeated.
    /// </summary>
    public void ResetEffectsTerminalFailure(Guid operatorUserId, DateTimeOffset now)
    {
        Status = StatusDispatchApplied;
        TerminalFailureAt = null;
        FailureCategory = null;
        FailureReason = null;
        AttemptCount = 0;
        ReleaseClaim();
        ResetCount++;
        LastResetAt = now;
        LastResetBy = operatorUserId;
    }

    public static TaskCompletionOperation CreatePending(
        Guid id,
        Guid companyId,
        Guid taskId,
        Guid completedBy,
        string? outcomeDecision,
        string? outcomeReason,
        DateTimeOffset now,
        IExecutionContext? executionContext = null) =>
        CreatePending(id, companyId, completedBy,
            TaskCompletionCommand.Create(taskId, outcomeDecision, outcomeReason), now, executionContext);

    public static TaskCompletionOperation CreatePending(
        Guid id,
        Guid companyId,
        Guid completedBy,
        TaskCompletionCommand command,
        DateTimeOffset now,
        IExecutionContext? executionContext = null)
    {
        return new TaskCompletionOperation
        {
            Id = id,
            CompanyId = companyId,
            TaskId = command.TaskId,
            CompletedBy = completedBy,
            OutcomeDecision = command.Decision,
            OutcomeReason = command.Reason,
            CommandFingerprint = command.Fingerprint(),
            Status = StatusPending,
            AttemptCount = 0,
            CreatedAt = now,
            Version = 1,
            CorrelationId = executionContext is null ? null : CorrelationIdGuid.Derive(executionContext.CorrelationId),
            CausationId = executionContext?.MessageId,
            MessageId = Guid.NewGuid(),
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

    public bool IsClaimedBy(Guid workerId, DateTimeOffset now) =>
        ClaimedBy == workerId && LeaseExpiresAt is { } expiresAt && expiresAt > now;

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

    public void MarkDataIntegrityFailure(string reason, DateTimeOffset now, string category = CategoryTaskMissing)
    {
        Status = StatusDataIntegrityFailure;
        TerminalFailureAt = now;
        FailureCategory = category;
        FailureReason = reason.Length > 500 ? reason[..500] : reason;
        LastAttemptAt = now;
        ReleaseClaim();
    }

    public void RecordFailure(string reason)
    {
        FailureReason = reason;
    }
}
