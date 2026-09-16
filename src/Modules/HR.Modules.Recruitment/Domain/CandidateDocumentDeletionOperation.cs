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
/// </summary>
internal sealed class CandidateDocumentDeletionOperation
{
    private CandidateDocumentDeletionOperation() { }

    public const string StatusPending    = "pending";
    public const string StatusProcessing = "processing";
    public const string StatusCompleted  = "completed";
    public const string StatusFailed     = "failed";

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
        };
    }

    public void MarkProcessing(DateTimeOffset now)
    {
        Status = StatusProcessing;
        AttemptCount++;
        LastAttemptAt = now;
    }

    public void MarkCompleted(DateTimeOffset now)
    {
        Status = StatusCompleted;
        CompletedAt = now;
        FailureReason = null;
    }

    /// <summary>Permanent failure after exhausting retries — the storage key, candidate and company
    /// context all remain on this row (never deleted) so it stays discoverable for manual
    /// remediation.</summary>
    public void MarkFailed(string reason, DateTimeOffset now)
    {
        Status = StatusFailed;
        LastAttemptAt = now;
        FailureReason = reason;
    }

    /// <summary>Resets a Failed record back to Pending so a retry sweep can re-enqueue it.</summary>
    public void ResetForRetry()
    {
        Status = StatusPending;
        FailureReason = null;
    }

    /// <summary>Resets a Processing record stuck past the sweep's staleness threshold — recovers
    /// from a crash mid-attempt.</summary>
    public void ResetToPendingAfterInterruption()
    {
        Status = StatusPending;
    }
}
