namespace HR.Modules.Reporting.Features.DashboardSummaries;

internal sealed record DashboardSummaryResponse(
    IReadOnlyList<DashboardCategoryResult> Categories,
    int TotalActionableCount,
    bool AllRequiredLoaded,
    bool HasPartialFailure,
    DateOnly AsOfDate,
    int TotalWaitingOnOthersCount = 0,
    IReadOnlyList<DashboardExceptionItem>? Exceptions = null,
    int TotalUnavailableCount = 0)
{
    public IReadOnlyList<DashboardExceptionItem> Exceptions { get; init; } = Exceptions ?? [];
}

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
    IReadOnlyList<DashboardActionItem> Items,
    IReadOnlyList<DashboardActionItem>? WaitingItems = null,
    int WaitingOnOthersCount = 0,
    bool WaitingIsTruncated = false,
    int UnavailableCount = 0)
{
    public IReadOnlyList<DashboardActionItem> WaitingItems { get; init; } = WaitingItems ?? [];
}

internal static class DashboardActionability
{
    public const string CanAct = "CanAct";
    public const string VisibilityOnly = "VisibilityOnly";
}

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
    string? OwnerLabel = null,
    string Actionability = DashboardActionability.CanAct,
    string? VisibilityReason = null,
    string? MonitoringUrl = null);

internal sealed record DashboardExceptionItem(
    string Category,
    string Message,
    string? EmployeeName,
    string? InvestigationUrl);
