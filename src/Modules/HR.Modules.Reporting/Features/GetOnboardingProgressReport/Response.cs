using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Features.GetOnboardingProgressReport;

internal sealed record GetOnboardingProgressReportResponse(
    IReadOnlyList<OnboardingProgressReportRow> Items,
    int TotalEmployees,
    int TotalOutstandingTasks,
    int OverdueEmployeeCount);

internal sealed record OnboardingProgressReportRow(
    Guid EmployeeId,
    string EmployeeName,
    string PlanStatus,
    int ProgressPercent,
    IReadOnlyList<OnboardingReportTaskItem> OutstandingTasks,
    bool HasOverdueTasks);
