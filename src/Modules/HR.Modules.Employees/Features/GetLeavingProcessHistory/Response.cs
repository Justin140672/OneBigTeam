namespace HR.Modules.Employees.Features.GetLeavingProcessHistory;

internal sealed record LeavingProcessHistoryItem(
    Guid Id,
    string Status,
    DateOnly ResignationReceivedDate,
    DateOnly LeavingDate,
    DateOnly LastWorkingDay,
    string LeavingReason,
    string? Notes,
    string? ReplacementManagerName,
    DateTimeOffset StartedAt,
    DateTimeOffset? CancelledAt,
    string? CancellationReason,
    DateTimeOffset? FinalisationCompletedAt,
    DateTimeOffset UpdatedAt,
    bool IsCurrent);

internal sealed record GetLeavingProcessHistoryResponse(IReadOnlyList<LeavingProcessHistoryItem> Items);
