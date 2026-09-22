namespace HR.Modules.Support.Domain;

/// <summary>
/// Reliability review issue 4 (P1): durable record of a support-attachment storage blob that failed
/// immediate best-effort deletion (e.g. during cleanup after a failed upload batch), so it can be
/// retried by <see cref="Jobs.SupportAttachmentPendingDeletionRetryJob"/> instead of leaking forever.
/// This is an internal operational/reconciliation record, not tenant-facing business data — it has
/// no company_id because a storage key alone is sufficient to complete the retry, mirroring other
/// infra-owned durable-retry tables in this codebase (e.g. Recruitment's
/// CandidateDocumentDeletionOperation, which does carry company_id because it also drives
/// company-scoped audit events; this table does not).
/// </summary>
internal sealed class SupportAttachmentPendingDeletion
{
    private SupportAttachmentPendingDeletion() { }

    public Guid Id { get; private set; }
    public string StorageKey { get; private set; } = string.Empty;
    public string? LastFailureReason { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }

    public static SupportAttachmentPendingDeletion Create(
        Guid id, string storageKey, string? failureReason, DateTimeOffset now)
    {
        return new SupportAttachmentPendingDeletion
        {
            Id = id,
            StorageKey = storageKey,
            LastFailureReason = failureReason,
            AttemptCount = 1,
            CreatedAt = now,
            LastAttemptAt = now,
        };
    }

    public void RecordRetryFailure(string? failureReason, DateTimeOffset now)
    {
        AttemptCount++;
        LastFailureReason = failureReason;
        LastAttemptAt = now;
    }

    public void MarkResolved(DateTimeOffset now)
    {
        ResolvedAt = now;
        LastAttemptAt = now;
    }
}
