namespace HR.Modules.Support.Domain;

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
