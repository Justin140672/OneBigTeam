namespace HR.Modules.Tasks.Contracts;

public enum TaskCompletionResetOutcome
{
    /// <summary>The terminal completion was reset and is eligible for reconciliation immediately.</summary>
    Reset,

    /// <summary>The completion exists but is not terminally failed (already reset, or confirmed); nothing changed.</summary>
    NotTerminal,

    NotFound,

    /// <summary>A concurrent writer changed the completion and it is still terminal; retry the request.</summary>
    Conflict,
}

public sealed record TaskCompletionResetResult(
    TaskCompletionResetOutcome Outcome,
    Guid OperationId,
    Guid? TaskId = null);

public interface ITaskCompletionRecovery
{
    /// <summary>
    /// Operator action: starts a new retry cycle for a terminally failed programmatic completion,
    /// preserving every confirmed effect checkpoint. Idempotent; the caller must already have
    /// authorised the operator.
    /// </summary>
    Task<TaskCompletionResetResult> ResetTerminalCompletionAsync(
        Guid companyId,
        Guid operationId,
        Guid operatorUserId,
        string reason,
        CancellationToken cancellationToken);
}
