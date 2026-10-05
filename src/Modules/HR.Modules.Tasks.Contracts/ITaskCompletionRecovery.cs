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

    /// <summary>The operation is flagged as a data-integrity failure; it needs investigation and cannot be reset.</summary>
    DataIntegrityFailure,
}

public sealed record TaskCompletionResetResult(
    TaskCompletionResetOutcome Outcome,
    Guid OperationId,
    Guid? TaskId = null,
    Guid? RecoveryActionId = null,
    int ResetCount = 0,
    string? OperationKind = null);

public interface ITaskCompletionRecovery
{
    /// <summary>
    /// Operator action: starts a new retry cycle for a terminally failed programmatic or interactive
    /// completion, preserving every confirmed effect checkpoint and never re-running a business
    /// dispatch. The reset and its durable audit intent commit in one transaction. Idempotent; the
    /// caller must already have authorised the operator.
    /// </summary>
    Task<TaskCompletionResetResult> ResetTerminalCompletionAsync(
        Guid companyId,
        Guid operationId,
        Guid operatorUserId,
        string reason,
        CancellationToken cancellationToken);
}
