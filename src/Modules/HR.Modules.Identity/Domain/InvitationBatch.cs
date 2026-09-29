namespace HR.Modules.Identity.Domain;

internal sealed class InvitationBatch
{
    private InvitationBatch() { }

    public const string StatusQueued     = "queued";
    public const string StatusProcessing = "processing";
    public const string StatusCompleted  = "completed";

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public string Status { get; private set; } = StatusQueued;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>
    /// Only set when the caller supplied an "Idempotency-Key" on the queue request. Unique per
    /// company (see InvitationBatchConfiguration's partial unique index) — this is what prevents a
    /// double-clicked/retried queue submission from creating two batches when a key is supplied.
    /// A caller that never supplies a key is not protected by this DB constraint (documented
    /// caller risk, deliberately not over-engineered further — see QueueInvitationBatchHandler).
    /// </summary>
    public string? IdempotencyKey { get; private set; }

    public static InvitationBatch Create(
        Guid companyId,
        Guid requestedByUserId,
        DateTimeOffset now,
        string? idempotencyKey)
    {
        return new InvitationBatch
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            RequestedByUserId = requestedByUserId,
            Status = StatusQueued,
            CreatedAt = now,
            IdempotencyKey = idempotencyKey,
        };
    }

    public void MarkProcessing(DateTimeOffset now)
    {
        Status = StatusProcessing;
        StartedAt ??= now;
    }

    public void MarkCompleted(DateTimeOffset now)
    {
        Status = StatusCompleted;
        CompletedAt = now;
    }

    public void ReopenForRetry()
    {
        Status = StatusQueued;
        CompletedAt = null;
    }
}
