namespace HR.Modules.Employees.Features.ListLeavingProcessPropagations;

internal sealed record ListLeavingProcessPropagationsResponse(
    int PendingCount,
    int FailedCount,
    int ProcessedCount,
    IReadOnlyList<LeavingProcessPropagationItem> Items);

internal sealed record LeavingProcessPropagationItem(
    Guid Id,
    Guid EmployeeId,
    Guid LeavingProcessId,
    string OperationType,
    DateOnly? LeavingDate,
    DateOnly? LastWorkingDay,
    string Status,
    int AttemptCount,
    string? LastError,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? NextAttemptAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ProcessedAt,
    Guid? CorrelationId,
    Guid? CausationId);
