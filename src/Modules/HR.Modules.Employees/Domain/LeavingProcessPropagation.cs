namespace HR.Modules.Employees.Domain;

internal sealed class LeavingProcessPropagation
{
    private LeavingProcessPropagation() { }

    public const string OperationStarted = "started";
    public const string OperationAmended = "amended";
    public const string OperationCancelled = "cancelled";
    public const string OperationReconciledStarted = "reconciled_started";
    public const string OperationReconciledCancelled = "reconciled_cancelled";

    public const string StatusPending = "pending";
    public const string StatusProcessed = "processed";
    public const string StatusFailed = "failed";

    public const int MaxAttempts = 10;

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public Guid LeavingProcessId { get; private set; }
    public string OperationType { get; private set; } = string.Empty;
    public DateOnly? LeavingDate { get; private set; }
    public DateOnly? LastWorkingDay { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string Status { get; private set; } = StatusPending;
    public int AttemptCount { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public DateTimeOffset? NextAttemptAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public Guid? CorrelationId { get; private set; }
    public Guid? CausationId { get; private set; }

    public static LeavingProcessPropagation Create(
        Guid companyId,
        Guid employeeId,
        Guid leavingProcessId,
        string operationType,
        DateOnly? leavingDate,
        DateOnly? lastWorkingDay,
        DateTimeOffset occurredAt,
        DateTimeOffset now,
        Guid? correlationId,
        Guid? causationId)
    {
        return new LeavingProcessPropagation
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            EmployeeId = employeeId,
            LeavingProcessId = leavingProcessId,
            OperationType = operationType,
            LeavingDate = leavingDate,
            LastWorkingDay = lastWorkingDay,
            OccurredAt = occurredAt,
            Status = StatusPending,
            CreatedAt = now,
            NextAttemptAt = now,
            CorrelationId = correlationId,
            CausationId = causationId,
        };
    }

    public bool IsCancellation => OperationType is OperationCancelled or OperationReconciledCancelled;

    public void MarkProcessed(DateTimeOffset now)
    {
        Status = StatusProcessed;
        AttemptCount++;
        LastAttemptAt = now;
        ProcessedAt = now;
        NextAttemptAt = null;
        LastError = null;
    }

    public void MarkAttemptFailed(string error, DateTimeOffset now)
    {
        AttemptCount++;
        LastAttemptAt = now;
        LastError = error.Length > 1000 ? error[..1000] : error;

        if (AttemptCount >= MaxAttempts)
        {
            Status = StatusFailed;
            NextAttemptAt = null;
            return;
        }

        NextAttemptAt = now + Backoff(AttemptCount);
    }

    public void ResetForRetry(DateTimeOffset now)
    {
        Status = StatusPending;
        AttemptCount = 0;
        NextAttemptAt = now;
    }

    private static TimeSpan Backoff(int attempt) =>
        TimeSpan.FromSeconds(Math.Min(300, Math.Pow(2, attempt)));
}
