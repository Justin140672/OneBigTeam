namespace HR.Modules.Reporting.Features.DashboardSummaries;

internal sealed record DashboardSummaryResponse(
    IReadOnlyList<DashboardCategoryResult> Categories,
    int TotalActionableCount,
    bool AllRequiredLoaded,
    bool HasPartialFailure,
    DateOnly AsOfDate);

internal enum DashboardCategoryStatus
{
    Loaded,
    Failed
}

internal sealed record DashboardCategoryResult(
    string Category,
    DashboardCategoryStatus Status,
    bool Required,
    int ActionableCount,
    bool IsTruncated,
    IReadOnlyList<DashboardActionItem> Items);

internal sealed record DashboardActionItem(
    Guid? EmployeeId,
    string EmployeeName,
    string? Department,
    string ActionType,
    string Category,
    DateOnly? DueDate,
    string Urgency,
    bool IsOverdue,
    string Status,
    string DeepLinkUrl,
    Guid? TaskId,
    bool IsOwnerActionable = true,
    string? OwnerLabel = null);
