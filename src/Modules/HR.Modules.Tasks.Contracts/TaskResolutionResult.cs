namespace HR.Modules.Tasks.Contracts;

public enum TaskResolutionStatus
{
    /// <summary>Every completion effect is confirmed (or there was nothing to complete).</summary>
    Confirmed,

    /// <summary>Effects are still outstanding (owned by another path, leased by another worker, or awaiting a retry).</summary>
    Outstanding,

    /// <summary>The completion failed permanently and will not be retried until an operator resets it.</summary>
    TerminalFailure,
}

/// <summary>
/// Outcome of driving a task completion to confirmation. <see cref="OperationId"/> identifies the
/// Tasks-side programmatic completion operation so an operator can locate and reset it.
/// </summary>
public sealed record TaskResolutionResult(
    TaskResolutionStatus Status,
    Guid? TaskId = null,
    Guid? OperationId = null,
    string? FailureReason = null,
    DateTimeOffset? TerminalFailureAt = null)
{
    public bool IsConfirmed => Status == TaskResolutionStatus.Confirmed;

    public static TaskResolutionResult Confirmed(Guid? taskId = null, Guid? operationId = null) =>
        new(TaskResolutionStatus.Confirmed, taskId, operationId);

    public static TaskResolutionResult Outstanding(Guid? taskId = null, Guid? operationId = null) =>
        new(TaskResolutionStatus.Outstanding, taskId, operationId);

    public static TaskResolutionResult Terminal(
        Guid taskId, Guid operationId, string? failureReason, DateTimeOffset? terminalFailureAt) =>
        new(TaskResolutionStatus.TerminalFailure, taskId, operationId, failureReason, terminalFailureAt);
}
