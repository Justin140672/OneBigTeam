namespace HR.Modules.Tasks.Domain;

/// <summary>
/// Ticket 4 (P1): a durable, observable record of a task-completion request, persisted alongside
/// the <see cref="TaskItem"/> transition so a crash/failure partway through completion's side
/// effects (notification, audit, downstream business dispatch) never leaves a completed task with
/// silently-lost decision data or a permanently-stuck notification/audit step.
///
/// Lifecycle:
///  - Pending: request received, decision payload persisted, business dispatch not yet attempted.
///  - Rejected: the business dispatch (see ITaskCompletionAction/TaskCompletionDispatcher) rejected
///    the decision — terminal, not retried; the underlying TaskItem was never marked Completed.
///  - DispatchApplied: the business dispatch succeeded and the TaskItem was marked Completed, but
///    the remaining side effects (notification write, audit publish) have not yet been confirmed —
///    either still in-flight or handed to <c>TaskCompletionEffectsJob</c> for retry.
///  - Processed: fully applied — business dispatch succeeded AND every side effect confirmed.
/// </summary>
internal sealed class TaskCompletionOperation
{
    private TaskCompletionOperation() { }

    public const string StatusPending         = "pending";
    public const string StatusRejected        = "rejected";
    public const string StatusDispatchApplied = "dispatch_applied";
    public const string StatusProcessed       = "processed";

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid TaskId { get; private set; }
    public Guid CompletedBy { get; private set; }
    public string? OutcomeDecision { get; private set; }
    public string? OutcomeReason { get; private set; }
    public string Status { get; private set; } = StatusPending;
    public int AttemptCount { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }

    public static TaskCompletionOperation CreatePending(
        Guid id,
        Guid companyId,
        Guid taskId,
        Guid completedBy,
        string? outcomeDecision,
        string? outcomeReason,
        DateTimeOffset now)
    {
        return new TaskCompletionOperation
        {
            Id = id,
            CompanyId = companyId,
            TaskId = taskId,
            CompletedBy = completedBy,
            OutcomeDecision = outcomeDecision,
            OutcomeReason = outcomeReason,
            Status = StatusPending,
            AttemptCount = 0,
            CreatedAt = now,
        };
    }

    public void MarkRejected(string reason, DateTimeOffset now)
    {
        Status = StatusRejected;
        FailureReason = reason;
        LastAttemptAt = now;
    }

    public void MarkDispatchApplied(DateTimeOffset now)
    {
        Status = StatusDispatchApplied;
        LastAttemptAt = now;
    }

    public void RecordAttempt(DateTimeOffset now)
    {
        AttemptCount++;
        LastAttemptAt = now;
    }

    public void MarkProcessed(DateTimeOffset now)
    {
        Status = StatusProcessed;
        ProcessedAt = now;
        FailureReason = null;
    }

    public void RecordFailure(string reason)
    {
        FailureReason = reason;
    }
}
