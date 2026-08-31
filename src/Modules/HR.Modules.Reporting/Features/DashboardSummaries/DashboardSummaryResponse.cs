namespace HR.Modules.Reporting.Features.DashboardSummaries;

/// <summary>
/// DSH-06 bounded dashboard summary envelope. Shared by the HR and Manager dashboard summary
/// endpoints — both compose the same cross-module <see cref="HR.Infrastructure.Abstractions.IWorkloadActionProvider"/>
/// fan-out via <see cref="DashboardSummaryComposer"/>; they differ only in the endpoint-level
/// authorization gate, not in the response shape.
/// </summary>
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
    Guid? TaskId);
