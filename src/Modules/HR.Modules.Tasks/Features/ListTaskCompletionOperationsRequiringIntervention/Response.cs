namespace HR.Modules.Tasks.Features.ListTaskCompletionOperationsRequiringIntervention;

internal sealed record TaskCompletionOperationInterventionItem(
    Guid OperationId,
    Guid TaskId,
    string Status,
    string? FailureCategory,
    DateTimeOffset? TerminalFailureAt,
    DateTimeOffset CreatedAt,
    int AttemptCount,
    int ResetCount,
    int AdjudicationCount,
    bool IsResettable,
    bool RequiresInvestigation,
    string RecoveryStatus,
    string? ResolutionType,
    DateTimeOffset? LastAdjudicatedAt);

internal sealed record ListTaskCompletionOperationsRequiringInterventionResponse(
    IReadOnlyList<TaskCompletionOperationInterventionItem> Items,
    int TotalCount,
    int PageNumber,
    int PageSize);
