namespace HR.Modules.Identity.Features.GetPermissionHistory;

internal sealed record PermissionHistoryItem(
    DateTimeOffset OccurredAt,
    string EventType,
    string Summary,
    string PerformedBy,
    Guid? TargetEmployeeId,
    string? PreviousAccess,
    string? NewAccess);

internal sealed record GetPermissionHistoryResponse(
    IReadOnlyList<PermissionHistoryItem> Items, int TotalCount, int Page, int PageSize);
