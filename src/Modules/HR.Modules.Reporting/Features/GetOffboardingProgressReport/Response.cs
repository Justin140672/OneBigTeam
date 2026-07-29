namespace HR.Modules.Reporting.Features.GetOffboardingProgressReport;

internal sealed record GetOffboardingProgressReportResponse(
    IReadOnlyList<OffboardingProgressReportRow> Items,
    int TotalEmployees,
    int OutstandingAccessCount,
    int OutstandingAssetsCount);

internal sealed record OffboardingProgressReportRow(
    Guid EmployeeId,
    string EmployeeName,
    DateOnly LastWorkingDay,
    string Status,
    IReadOnlyList<string> OutstandingTasks,
    IReadOnlyList<string> CompletedTasks,
    bool AccessDisabled,
    bool DocumentsReturned,
    bool AssetsReturned);
