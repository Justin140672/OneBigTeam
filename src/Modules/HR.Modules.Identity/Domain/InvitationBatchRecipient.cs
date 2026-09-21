namespace HR.Modules.Identity.Domain;

/// <summary>
/// One recipient of a bulk invitation batch (see <see cref="InvitationBatch"/>). Email is a
/// server-resolved snapshot taken at queue time — never client supplied. <see cref="InviteId"/> is
/// only set once a <see cref="UserInvite"/> has actually been created for this recipient, so a retry
/// of email delivery (ProcessInvitationBatchJob) never creates a second UserInvite for the same
/// recipient.
/// </summary>
internal sealed class InvitationBatchRecipient
{
    private InvitationBatchRecipient() { }

    public const string StatusWaiting    = "waiting";
    public const string StatusProcessing = "processing";
    public const string StatusSent       = "sent";
    public const string StatusSkipped    = "skipped";
    public const string StatusFailed     = "failed";

    public Guid Id { get; private set; }
    public Guid BatchId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string Status { get; private set; } = StatusWaiting;
    public string? FailureReason { get; private set; }
    public Guid? InviteId { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static InvitationBatchRecipient Create(
        Guid batchId,
        Guid employeeId,
        string email,
        DateTimeOffset now)
    {
        return new InvitationBatchRecipient
        {
            Id = Guid.NewGuid(),
            BatchId = batchId,
            EmployeeId = employeeId,
            Email = email,
            Status = StatusWaiting,
            CreatedAt = now,
        };
    }

    public void MarkProcessing()
    {
        Status = StatusProcessing;
        FailureReason = null;
    }

    /// <summary>Records that a UserInvite has been created for this recipient — must be set (and
    /// saved) before attempting email delivery, so a crash between invite-creation and email-send
    /// never results in a second UserInvite being created on retry.</summary>
    public void RecordInviteCreated(Guid inviteId)
    {
        InviteId = inviteId;
    }

    public void MarkSent(DateTimeOffset now)
    {
        Status = StatusSent;
        FailureReason = null;
        ProcessedAt = now;
    }

    public void MarkSkipped(string reason, DateTimeOffset now)
    {
        Status = StatusSkipped;
        FailureReason = reason;
        ProcessedAt = now;
    }

    public void MarkFailed(string reason)
    {
        Status = StatusFailed;
        FailureReason = reason;
    }

    /// <summary>Resets a Failed recipient back to Waiting so RetryInvitationBatch can re-process it.
    /// Deliberately does not touch <see cref="InviteId"/> — if an invite was already created, the
    /// job's retry logic reuses it rather than creating a second one.</summary>
    public void ResetForRetry()
    {
        Status = StatusWaiting;
        FailureReason = null;
        ProcessedAt = null;
    }
}
