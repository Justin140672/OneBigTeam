using HR.SharedKernel;

namespace HR.Modules.Tasks.Domain;

/// <summary>
/// Durable operator-recovery audit intent, saved in the same transaction as the state change it
/// describes. <see cref="Id"/> is the stable audit event id, so every delivery attempt converges on
/// one audit record. Delivery is confirmed through the audit existence reader, never inferred from
/// a normal return of the publisher.
/// </summary>
internal sealed class TaskRecoveryAction
{
    public const string KindProgrammatic = "programmatic";
    public const string KindInteractive = "interactive";

    public const string ActionReset = "reset";
    public const string ActionAdjudication = "adjudication";

    public static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);

    private TaskRecoveryAction() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid TaskId { get; private set; }
    public Guid OperationId { get; private set; }
    public string OperationKind { get; private set; } = KindProgrammatic;
    public string ActionType { get; private set; } = ActionReset;
    public string? ResolutionType { get; private set; }
    public string? PreviousStatus { get; private set; }
    public string? ResultingStatus { get; private set; }
    public bool EvidenceSupplied { get; private set; }
    public Guid OperatorUserId { get; private set; }
    public string Reason { get; private set; } = string.Empty;
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

    public static Guid EventIdFor(Guid operationId, int sequenceNumber) =>
        DeterministicGuid.From($"task-completion-reset:{operationId:N}:{sequenceNumber}");

    public static Guid AdjudicationEventIdFor(Guid operationId, int sequenceNumber) =>
        DeterministicGuid.From($"task-completion-adjudication:{operationId:N}:{sequenceNumber}");

    public static TaskRecoveryAction CreateAdjudication(
        Guid companyId, Guid taskId, Guid operationId, Guid operatorUserId, string reason, int sequenceNumber,
        string resolutionType, string previousStatus, string resultingStatus, bool evidenceSupplied,
        DateTimeOffset now, Guid? correlationId)
    {
        var action = Create(
            companyId, taskId, operationId, KindInteractive, operatorUserId, reason, sequenceNumber, now, correlationId);
        action.Id = AdjudicationEventIdFor(operationId, sequenceNumber);
        action.ActionType = ActionAdjudication;
        action.ResolutionType = resolutionType;
        action.PreviousStatus = previousStatus;
        action.ResultingStatus = resultingStatus;
        action.EvidenceSupplied = evidenceSupplied;
        return action;
    }

    public static TaskRecoveryAction Create(
        Guid companyId, Guid taskId, Guid operationId, string operationKind, Guid operatorUserId,
        string reason, int sequenceNumber, DateTimeOffset now, Guid? correlationId) =>
        new()
        {
            Id = EventIdFor(operationId, sequenceNumber),
            CompanyId = companyId,
            TaskId = taskId,
            OperationId = operationId,
            OperationKind = operationKind,
            OperatorUserId = operatorUserId,
            Reason = reason,
            SequenceNumber = sequenceNumber,
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
