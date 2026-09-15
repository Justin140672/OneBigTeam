namespace HR.Modules.Leave.Domain;

/// <summary>
/// A durable, module-scoped record of a required <see cref="EmployeeLeavePolicyAssignment"/>
/// deactivation triggered by an employee's departure being finalised (see
/// Features/DeactivateLeavePolicyAssignmentOnEmployeeDeparture/EmployeeDepartureFinalisedHandler.cs
/// and Jobs/LeavePolicyDeactivationJob.cs).
///
/// Follow-up fix: EmployeeDepartureFinalisedIntegrationEvent is dispatched in-process by
/// HR.SharedKernel.IntegrationEventPublisher, which deliberately catches and only logs a handler's
/// exception so one failing consumer never blocks another, or the publishing transaction. That
/// means a transient failure inside the old inline deactivation was silently dropped — nothing
/// would ever retry it, yet Employees' EmployeeLeavingProcess.FinalisationCompletedAt would still
/// (correctly, per its own contract — see that property's remarks) be marked complete, since it
/// only guarantees the publish call returned, not that every consumer's downstream work finished.
///
/// This record makes the resulting deactivation durable and retryable, mirroring
/// HR.Modules.Identity.Domain.AccountDisablement's Pending -&gt; Processing -&gt; Processed|Failed shape,
/// so a transient failure never silently leaves a departed employee's leave policy assignment
/// active, and success is only ever recorded once the assignment has actually been deactivated.
///
/// OccurredAt is captured from the originating integration event at creation time and reused by
/// the processing job on every attempt/replay, so a retried deactivation is stamped with the
/// original departure timing rather than a new "now" that would shift the audit trail.
/// </summary>
internal sealed class LeavePolicyDeactivationOnDeparture
{
    private LeavePolicyDeactivationOnDeparture() { }

    public const string StatusPending    = "pending";
    public const string StatusProcessing = "processing";
    public const string StatusProcessed  = "processed";
    public const string StatusFailed     = "failed";

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public int AttemptCount { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }

    public static LeavePolicyDeactivationOnDeparture CreatePending(
        Guid id,
        Guid companyId,
        Guid employeeId,
        DateTimeOffset occurredAt,
        DateTimeOffset requestedAt)
    {
        return new LeavePolicyDeactivationOnDeparture
        {
            Id = id,
            CompanyId = companyId,
            EmployeeId = employeeId,
            OccurredAt = occurredAt,
            Status = StatusPending,
            AttemptCount = 0,
            RequestedAt = requestedAt,
        };
    }

    public void MarkProcessing(DateTimeOffset now)
    {
        Status = StatusProcessing;
        AttemptCount++;
        LastAttemptAt = now;
        FailureReason = null;
    }

    public void MarkProcessed(DateTimeOffset now)
    {
        Status = StatusProcessed;
        ProcessedAt = now;
        FailureReason = null;
    }

    /// <summary>Final, visible, detectable failure state — never a silently stuck "processing"
    /// row. Left for a support/HR action or the reconciliation sweep to pick up.</summary>
    public void MarkFailed(string reason, DateTimeOffset now)
    {
        Status = StatusFailed;
        LastAttemptAt = now;
        FailureReason = reason;
    }

    /// <summary>Resets a Failed record back to Pending so a retry sweep can re-enqueue it.</summary>
    public void ResetForRetry()
    {
        if (Status != StatusFailed)
            throw new InvalidOperationException($"Cannot retry a leave policy deactivation with status '{Status}'.");

        Status = StatusPending;
        FailureReason = null;
    }
}
