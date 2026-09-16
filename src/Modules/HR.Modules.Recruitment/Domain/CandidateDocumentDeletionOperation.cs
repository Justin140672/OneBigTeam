using HR.SharedKernel;

namespace HR.Modules.Recruitment.Domain;

/// <summary>
/// Ticket 13 (P2): a durable, per-storage-key record of a candidate document blob deletion owed by
/// a candidate purge, created in the SAME transaction that deletes the owning
/// <see cref="CandidateDocument"/> row and redacts the candidate (see
/// Features/PurgeEligibleCandidates/Handler.cs). Previously the storage key existed only in memory
/// between that commit and the Hangfire enqueue call — a crash in that window permanently lost it,
/// since the CandidateDocument row (the only other place it lived) had already been deleted.
///
/// A recurring worker (see Jobs/PurgeCandidateDocumentStorageReconciliationJob.cs) claims Pending/
/// stale-Processing/retryable-Failed operations and drives them to Completed — Hangfire's own
/// immediate enqueue (still done right after the same save, in
/// PurgeEligibleCandidatesHandler) remains as a latency optimisation only; correctness comes from
/// this table.
///
/// Ticket 19 (P2): implements <see cref="IVersionedAggregate"/> — same claim/lease idiom as
/// <see cref="HR.Modules.Identity.Domain.AccountDisablement"/>. Previously staleness was inferred
/// from <c>LastAttemptAt</c> age with no concurrency token at all, so two reconciler replicas (or a
/// live enqueue racing a reconciliation re-enqueue) could both read and both act on the same row.
/// <see cref="Claim"/> is only ever persisted via
/// <see cref="DbContextConcurrencyExtensions.SaveChangesWithConcurrencyAsync{T}"/>, so exactly one
/// caller's claim for a given row can ever win.
/// </summary>
internal sealed class CandidateDocumentDeletionOperation : IVersionedAggregate
{
    private CandidateDocumentDeletionOperation() { }

    public const string StatusPending    = "pending";
    public const string StatusProcessing = "processing";
    public const string StatusCompleted  = "completed";
    public const string StatusFailed     = "failed";

    /// <summary>
    /// Ticket 18 (P1): the owning company is under a legal hold at the moment
    /// PurgeCandidateDocumentStorageJob was about to perform the actual destructive storage delete.
    /// Distinct from <see cref="StatusFailed"/> — this is not an error, consumes no retry attempt,
    /// and must not be repeatedly re-enqueued by the reconciliation sweep while the hold remains in
    /// effect. The storage key is retained exactly as-is so deletion can resume once the hold lifts.
    /// </summary>
    public const string StatusHeld = "held";

    /// <summary>Ticket 19 (P2): same generous-relative-to-normal-runtime lease duration as
    /// AccountDisablement.LeaseDuration — see that type's remarks for the reasoning.</summary>
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(10);

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid CandidateId { get; private set; }
    public string StorageKey { get; private set; } = string.Empty;
    public string Status { get; private set; } = StatusPending;
    public int AttemptCount { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    // Ticket 19 (P2): explicit, persisted optimistic-concurrency token — see AssetCategory.Version
    // for the same established idiom.
    public int Version { get; private set; } = 1;

    public Guid? ClaimedBy { get; private set; }
    public DateTimeOffset? LeaseExpiresAt { get; private set; }
    public bool IsTerminallyFailed { get; private set; }

    public Guid? LastRetriedByActorId { get; private set; }
    public string? LastRetryReason { get; private set; }
    public DateTimeOffset? LastRetriedAt { get; private set; }

    public void IncrementVersion() => Version++;

    public static CandidateDocumentDeletionOperation CreatePending(
        Guid id, Guid companyId, Guid candidateId, string storageKey, DateTimeOffset now)
    {
        return new CandidateDocumentDeletionOperation
        {
            Id = id,
            CompanyId = companyId,
            CandidateId = candidateId,
            StorageKey = storageKey,
            Status = StatusPending,
            AttemptCount = 0,
            CreatedAt = now,
            Version = 1,
        };
    }

    /// <summary>
    /// Ticket 19 (P2): claims this row for <paramref name="workerId"/> for
    /// <see cref="LeaseDuration"/> and transitions it to Processing — callers must persist this via
    /// <see cref="DbContextConcurrencyExtensions.SaveChangesWithConcurrencyAsync{T}"/> with the
    /// version this instance was loaded at. Works from ANY starting status (Pending, non-terminal
    /// Failed, stale Processing, Held-resumed) — the concurrency token, not the prior status, is
    /// what actually guarantees exclusivity.
    /// </summary>
    public void Claim(Guid workerId, DateTimeOffset now)
    {
        ClaimedBy = workerId;
        LeaseExpiresAt = now + LeaseDuration;
        Status = StatusProcessing;
        AttemptCount++;
        LastAttemptAt = now;
        FailureReason = null;
    }

    /// <summary>True when <paramref name="workerId"/> is the current, non-expired claim holder.</summary>
    public bool IsClaimedBy(Guid workerId, DateTimeOffset now) =>
        ClaimedBy == workerId && LeaseExpiresAt is { } expiresAt && expiresAt > now;

    public void MarkCompleted(DateTimeOffset now)
    {
        Status = StatusCompleted;
        CompletedAt = now;
        FailureReason = null;
        ClaimedBy = null;
        LeaseExpiresAt = null;
    }

    /// <summary>Bounded automatic retry (ticket 19): reaching <paramref name="maxAutomaticAttempts"/>
    /// marks this terminally failed — never reset by the reconciliation sweep again, requiring an
    /// explicit <see cref="RecordManualRetry"/>. The storage key, candidate and company context all
    /// remain on this row (never deleted) so it stays discoverable for manual remediation.</summary>
    public void MarkFailed(string reason, DateTimeOffset now, int maxAutomaticAttempts)
    {
        Status = StatusFailed;
        LastAttemptAt = now;
        FailureReason = reason;
        ClaimedBy = null;
        LeaseExpiresAt = null;

        if (AttemptCount >= maxAutomaticAttempts)
            IsTerminallyFailed = true;
    }

    /// <summary>Resets a Failed record back to Pending so a retry sweep can re-enqueue it. Refuses a
    /// terminally-failed record (ticket 19) — that requires <see cref="RecordManualRetry"/> instead.</summary>
    public void ResetForRetry()
    {
        if (Status != StatusFailed)
            throw new InvalidOperationException($"Cannot retry a document deletion operation with status '{Status}'.");

        if (IsTerminallyFailed)
            throw new InvalidOperationException(
                "This document deletion operation has exhausted its automatic retry budget and requires an explicit manual retry.");

        Status = StatusPending;
        FailureReason = null;
    }

    /// <summary>
    /// Ticket 19 (P2): explicit administrative override for a terminally-failed record — the only
    /// path back to Pending once <see cref="IsTerminallyFailed"/> is set. Always records who
    /// authorised it and why.
    /// </summary>
    public void RecordManualRetry(Guid actorId, string reason, DateTimeOffset now)
    {
        if (Status != StatusFailed)
            throw new InvalidOperationException($"Cannot manually retry a document deletion operation with status '{Status}'.");

        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A reason is required for a manual retry.", nameof(reason));

        Status = StatusPending;
        FailureReason = null;
        IsTerminallyFailed = false;
        LastRetriedByActorId = actorId;
        LastRetryReason = reason;
        LastRetriedAt = now;
    }

    /// <summary>
    /// Ticket 18 (P1): suspends this operation because the owning company is currently under legal
    /// hold — called from the worker immediately before the destructive storage delete would
    /// otherwise happen. Deliberately does NOT touch <see cref="AttemptCount"/> or
    /// <see cref="FailureReason"/> — a hold is not a failure, and must never count against (or be
    /// confused with) this operation's genuine retry budget. Releases the claim (ticket 19) — a
    /// held operation is not "owned" by anyone; the reconciliation sweep is what re-claims it once
    /// the hold lifts.
    /// </summary>
    public void MarkHeld(DateTimeOffset now)
    {
        Status = StatusHeld;
        LastAttemptAt = now;
        ClaimedBy = null;
        LeaseExpiresAt = null;
    }

    /// <summary>
    /// Ticket 18 (P1): the legal hold that suspended this operation has been confirmed lifted —
    /// makes it eligible for deletion again by returning it to Pending, for the reconciliation
    /// sweep (or the next direct enqueue) to pick up normally.
    /// </summary>
    public void ResumeFromHeld()
    {
        Status = StatusPending;
    }
}
