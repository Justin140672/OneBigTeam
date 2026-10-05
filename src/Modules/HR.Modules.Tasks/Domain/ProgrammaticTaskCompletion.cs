using HR.SharedKernel;

namespace HR.Modules.Tasks.Domain;

internal sealed class ProgrammaticTaskCompletion : IVersionedAggregate
{
    public const string DispatchModeDispatch = "dispatch";
    public const string DispatchModeAlreadyApplied = "already_applied";

    public static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);

    private ProgrammaticTaskCompletion() { }

    public Guid TaskId { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid CompletedBy { get; private set; }
    public string PreviousStatus { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? NotificationsClearedAt { get; private set; }
    public DateTimeOffset? CompletionNotificationAt { get; private set; }
    public DateTimeOffset? AuditPublishedAt { get; private set; }
    public DateTimeOffset? DispatchedAt { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }

    /// <summary>Stable identity passed to dispatched actions as <c>DispatchOperationId</c>.</summary>
    public Guid OperationId { get; private set; }
    public string DispatchMode { get; private set; } = DispatchModeDispatch;
    public int Version { get; private set; } = 1;
    public Guid? ClaimedBy { get; private set; }
    public DateTimeOffset? ClaimedUntil { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset? TerminalFailureAt { get; private set; }
    public int ResetCount { get; private set; }
    public DateTimeOffset? LastResetAt { get; private set; }
    public Guid? LastResetBy { get; private set; }

    public bool IsTerminallyFailed => TerminalFailureAt is not null;

    public void IncrementVersion() => Version++;

    public static ProgrammaticTaskCompletion Create(
        Guid taskId, Guid companyId, Guid completedBy, string previousStatus, DateTimeOffset now,
        bool businessEffectAlreadyApplied = false)
    {
        var state = new ProgrammaticTaskCompletion
        {
            TaskId = taskId,
            CompanyId = companyId,
            CompletedBy = completedBy,
            PreviousStatus = previousStatus,
            CreatedAt = now,
            OperationId = Guid.NewGuid(),
        };

        if (businessEffectAlreadyApplied)
        {
            state.DispatchMode = DispatchModeAlreadyApplied;
            state.DispatchedAt = now;
        }

        return state;
    }

    public bool IsLeased(DateTimeOffset now) =>
        ClaimedBy is not null && ClaimedUntil is { } until && until > now;

    public void Claim(Guid workerId, DateTimeOffset now)
    {
        ClaimedBy = workerId;
        ClaimedUntil = now + LeaseDuration;
        AttemptCount++;
        LastAttemptAt = now;
    }

    public void ReleaseClaim()
    {
        ClaimedBy = null;
        ClaimedUntil = null;
    }

    public void MarkNotificationsCleared(DateTimeOffset now) => NotificationsClearedAt = now;
    public void MarkCompletionNotificationWritten(DateTimeOffset now) => CompletionNotificationAt = now;
    public void MarkAuditPublished(DateTimeOffset now) => AuditPublishedAt = now;
    public void MarkDispatched(DateTimeOffset now)
    {
        DispatchedAt = now;
        FailureReason = null;
    }

    public void Confirm(DateTimeOffset now)
    {
        ConfirmedAt = now;
        FailureReason = null;
        ReleaseClaim();
    }

    public void RecordFailure(string reason, bool terminal, DateTimeOffset now)
    {
        FailureReason = reason.Length > 500 ? reason[..500] : reason;
        if (terminal)
            TerminalFailureAt = now;
        ReleaseClaim();
    }

    /// <summary>
    /// Operator recovery: starts a new retry cycle. Completed effect checkpoints are deliberately
    /// kept so confirmed effects are never repeated.
    /// </summary>
    public void ResetTerminalFailure(Guid operatorUserId, DateTimeOffset now)
    {
        TerminalFailureAt = null;
        FailureReason = null;
        AttemptCount = 0;
        ReleaseClaim();
        ResetCount++;
        LastResetAt = now;
        LastResetBy = operatorUserId;
    }
}
