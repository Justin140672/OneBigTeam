namespace HR.Modules.Reporting.Features.GetLeaveCalendarReport;

internal sealed record GetLeaveCalendarReportResponse(
    IReadOnlyList<LeaveCalendarReportRow> Items,
    int TotalCount,
    bool IsTruncated);

internal sealed record LeaveCalendarReportRow(
    Guid EmployeeId,
    string EmployeeName,
    string? Department,
    DateOnly LeaveStart,
    DateOnly LeaveEnd,
    string LeaveTypeName,
    decimal DurationDays,
    string ApprovalStatus);
