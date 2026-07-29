namespace HR.Modules.Reporting.Features.GetLeaveCalendarReport;

internal sealed record GetLeaveCalendarReportRequest(
    Guid CompanyId,
    int Year,
    int Month,
    Guid? DepartmentId = null);
