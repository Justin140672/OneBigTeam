namespace HR.Modules.Identity.Domain;

/// <summary>
/// A durable, module-scoped record of a required <see cref="ApplicationUser"/> disablement
/// triggered by an employee's departure being finalised (see
/// Features/OnEmployeeDepartureFinalised/Handler.cs and Jobs/AccountDisablementJob.cs).
///
/// P1 fix: departure finalisation previously only flipped Employees.Employee.HasSystemAccess,
/// while authentication actually enforces ApplicationUser.IsActive (see
/// DisabledAccountMiddleware) — the two could disagree indefinitely. This record makes the
/// resulting ApplicationUser update durable and retryable (mirrors
/// HR.Modules.Companies.Domain.OutboxMessage's Pending -&gt; Processing -&gt; Processed|Failed shape),
/// so a transient failure never silently leaves a departed employee's account active, and success
/// is only ever recorded once the account used by authentication has actually been disabled.
/// </summary>
internal sealed class AccountDisablement
{
    private AccountDisablement() { }

    public const string StatusPending    = "pending";
    public const string StatusProcessing = "processing";
    public const string StatusProcessed  = "processed";
    public const string StatusFailed     = "failed";

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ApplicationUserId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public int AttemptCount { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }

    public static AccountDisablement CreatePending(
        Guid id,
        Guid companyId,
        Guid applicationUserId,
        Guid employeeId,
        DateTimeOffset requestedAt)
    {
        return new AccountDisablement
        {
            Id = id,
            CompanyId = companyId,
            ApplicationUserId = applicationUserId,
            EmployeeId = employeeId,
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
    /// row. Left for a support/HR action or a retry sweep to pick up.</summary>
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
            throw new InvalidOperationException($"Cannot retry an account disablement with status '{Status}'.");

        Status = StatusPending;
        FailureReason = null;
    }

    /// <summary>
    /// Ticket 10 (P1): resets a Processing record back to Pending after it has been detected as
    /// stuck (no progress within <see cref="Jobs.AccountDisablementReconciliationJob"/>'s stale
    /// threshold) — used to recover from a crash that occurred mid-attempt, between MarkProcessing
    /// and the job's own MarkProcessed/MarkFailed call.
    /// </summary>
    public void ResetToPendingAfterInterruption()
    {
        if (Status != StatusProcessing)
            throw new InvalidOperationException(
                $"Cannot reset an account disablement with status '{Status}' as interrupted.");

        Status = StatusPending;
    }
}
