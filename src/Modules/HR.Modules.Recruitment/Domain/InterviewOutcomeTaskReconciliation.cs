namespace HR.Modules.Recruitment.Domain;

internal sealed class InterviewOutcomeTaskReconciliation
{
    public const string BlockedTaskTerminalFailure = "task_terminal_failure";
    public const string BlockedAuditSourceDataMissing = "audit_source_data_missing";

    public static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);

    private InterviewOutcomeTaskReconciliation() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ApplicationId { get; private set; }
    public Guid InterviewId { get; private set; }
    public Guid RecordedBy { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? ClaimedUntil { get; private set; }
    public DateTimeOffset? AuditDeliveredAt { get; private set; }
    public int Version { get; private set; }

    /// <summary>Set when reconciliation cannot progress without operator action; blocked records are excluded from sweeps.</summary>
    public DateTimeOffset? BlockedAt { get; private set; }
    public string? BlockedCategory { get; private set; }
    public Guid? BlockedTaskId { get; private set; }
    public Guid? BlockedTasksOperationId { get; private set; }
    public int RepairCount { get; private set; }
    public DateTimeOffset? LastRepairedAt { get; private set; }
    public Guid? LastRepairedBy { get; private set; }

    public bool IsBlocked => BlockedAt is not null;

    public void MarkAuditDelivered(DateTimeOffset now) => AuditDeliveredAt = now;

    public static InterviewOutcomeTaskReconciliation Create(
        Guid id, Guid companyId, Guid applicationId, Guid interviewId, Guid recordedBy, DateTimeOffset now) =>
        new()
        {
            Id = id,
            CompanyId = companyId,
            ApplicationId = applicationId,
            InterviewId = interviewId,
            RecordedBy = recordedBy,
            CreatedAt = now,
        };

    public bool TryClaim(DateTimeOffset now)
    {
        if (CompletedAt is not null || BlockedAt is not null || (ClaimedUntil is not null && ClaimedUntil > now))
            return false;

        ClaimedUntil = now + LeaseDuration;
        Version++;
        return true;
    }

    public void MarkCompleted(DateTimeOffset now)
    {
        AttemptCount++;
        LastAttemptAt = now;
        CompletedAt = now;
        ClaimedUntil = null;
        FailureReason = null;
    }

    public void MarkFailed(string reason, DateTimeOffset now)
    {
        AttemptCount++;
        LastAttemptAt = now;
        ClaimedUntil = null;
        FailureReason = Truncate(reason);
    }

    public void Block(
        string category, string reason, Guid? taskId, Guid? tasksOperationId, DateTimeOffset now)
    {
        AttemptCount++;
        LastAttemptAt = now;
        ClaimedUntil = null;
        FailureReason = Truncate(reason);
        BlockedAt = now;
        BlockedCategory = category;
        BlockedTaskId = taskId;
        BlockedTasksOperationId = tasksOperationId;
    }

    /// <summary>Clears the blocked state so ordinary reconciliation resumes; completed effects are untouched.</summary>
    public void Unblock(Guid operatorUserId, DateTimeOffset now)
    {
        BlockedAt = null;
        BlockedCategory = null;
        BlockedTaskId = null;
        BlockedTasksOperationId = null;
        FailureReason = null;
        ClaimedUntil = null;
        RepairCount++;
        LastRepairedAt = now;
        LastRepairedBy = operatorUserId;
        Version++;
    }

    private static string Truncate(string reason) => reason.Length > 500 ? reason[..500] : reason;
}
