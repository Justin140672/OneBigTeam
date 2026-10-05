namespace HR.Modules.Tasks.Contracts;

public sealed record TaskCompletionOperationState(
    Guid OperationId,
    Guid TaskId,
    bool IsTerminal,
    bool IsDataIntegrityFailure,
    int ResetCount,
    Guid? LastResetBy,
    DateTimeOffset? LastResetAt,
    int AdjudicationCount = 0,
    string? ResolutionType = null,
    bool IsWaived = false,
    Guid? LastAdjudicatedBy = null);

public interface ITaskCompletionOperationStateReader
{
    /// <summary>Company-scoped: an operation belonging to another company is reported as absent.</summary>
    Task<TaskCompletionOperationState?> GetAsync(
        Guid companyId, Guid operationId, CancellationToken cancellationToken);
}
