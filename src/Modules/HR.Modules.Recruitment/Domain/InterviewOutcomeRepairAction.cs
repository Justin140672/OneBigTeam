using HR.SharedKernel;

namespace HR.Modules.Recruitment.Domain;

/// <summary>
/// Durable audit intent for a repair of a blocked <see cref="InterviewOutcomeTaskReconciliation"/>,
/// saved in the same transaction as the unblock. <see cref="Id"/> is the stable audit event id, so
/// every delivery attempt converges on one audit record; delivery is confirmed through the audit
/// existence reader, never inferred from a normal return of the publisher.
/// </summary>
internal sealed class InterviewOutcomeRepairAction
{
    public const string SourceOperator = "operator";
    public const string SourceTasksReset = "tasks_reset";
    public const string SourceTasksAdjudication = "tasks_adjudication";

    public static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);

    private InterviewOutcomeRepairAction() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ReconciliationId { get; private set; }
    public Guid InterviewId { get; private set; }
    public Guid ApplicationId { get; private set; }
    public Guid? TasksOperationId { get; private set; }
    public string BlockedCategory { get; private set; } = string.Empty;
    public Guid OperatorUserId { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string Source { get; private set; } = SourceOperator;
    public int SequenceNumber { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public Guid? CorrelationId { get; private set; }
    public DateTimeOffset? AuditDeliveredAt { get; private set; }
    public int AuditAttemptCount { get; private set; }
    public DateTimeOffset? LastAuditAttemptAt { get; private set; }
    public string? LastAuditFailure { get; private set; }
    public DateTimeOffset? ClaimedUntil { get; private set; }
    public int Version { get; private set; }

    public bool IsDelivered => AuditDeliveredAt is not null;

    public static Guid EventIdFor(Guid reconciliationId, int sequenceNumber) =>
        DeterministicGuid.From($"interview-outcome-reconciliation-repair:{reconciliationId:N}:{sequenceNumber}");

    public static InterviewOutcomeRepairAction Create(
        InterviewOutcomeTaskReconciliation record,
        string blockedCategory,
        Guid? tasksOperationId,
        Guid operatorUserId,
        string reason,
        string source,
        DateTimeOffset now,
        Guid? correlationId) =>
        new()
        {
            Id = EventIdFor(record.Id, record.RepairCount),
            CompanyId = record.CompanyId,
            ReconciliationId = record.Id,
            InterviewId = record.InterviewId,
            ApplicationId = record.ApplicationId,
            TasksOperationId = tasksOperationId,
            BlockedCategory = blockedCategory,
            OperatorUserId = operatorUserId,
            Reason = reason.Length > 500 ? reason[..500] : reason,
            Source = source,
            SequenceNumber = record.RepairCount,
            OccurredAt = now,
            CorrelationId = correlationId,
        };

    public bool TryClaim(DateTimeOffset now)
    {
        if (IsDelivered || (ClaimedUntil is { } until && until > now))
            return false;

        ClaimedUntil = now + LeaseDuration;
        Version++;
        return true;
    }

    public void MarkDelivered(DateTimeOffset now)
    {
        AuditDeliveredAt = now;
        AuditAttemptCount++;
        LastAuditAttemptAt = now;
        LastAuditFailure = null;
        ClaimedUntil = null;
    }

    public void MarkDeliveryFailed(string failure, DateTimeOffset now)
    {
        AuditAttemptCount++;
        LastAuditAttemptAt = now;
        LastAuditFailure = failure.Length > 500 ? failure[..500] : failure;
        ClaimedUntil = null;
    }
}
