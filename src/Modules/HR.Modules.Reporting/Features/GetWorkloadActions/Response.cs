using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Features.GetWorkloadActions;

internal sealed record GetWorkloadActionsResponse(
    IReadOnlyList<WorkloadActionRow> Items,
    IReadOnlyList<WorkloadActionGroup> Groups,
    WorkloadActionSummary Summary,
    int TotalCount,
    bool IsTruncated);

internal sealed record WorkloadActionRow(
    Guid EmployeeId,
    string EmployeeName,
    string? Department,
    string ActionType,
    string ActionCategory,
    DateOnly? DueDate,
    string? AssignedTo,
    string Status,
    string Urgency,
    string DeepLinkUrl);

internal sealed record WorkloadActionGroup(
    string Key,
    IReadOnlyList<WorkloadActionRow> Items);

internal sealed record WorkloadActionSummary(
    int TotalOutstanding,
    int Overdue,
    int DueToday,
    int DueThisWeek);
