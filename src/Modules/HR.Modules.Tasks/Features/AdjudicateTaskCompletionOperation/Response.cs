namespace HR.Modules.Tasks.Features.AdjudicateTaskCompletionOperation;

internal sealed record AdjudicateTaskCompletionOperationResponse(
    Guid OperationId,
    Guid? TaskId,
    string? Status,
    string? ResolutionType,
    bool WasApplied,
    Guid? RecoveryActionId,
    int AdjudicationCount);
