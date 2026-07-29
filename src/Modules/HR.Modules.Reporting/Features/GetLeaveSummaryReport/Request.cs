namespace HR.Modules.Reporting.Features.GetLeaveSummaryReport;

internal enum LeaveSummaryGroupBy
{
    Employee = 1,
    Department = 2,
    LeaveType = 3,
}

internal sealed record GetLeaveSummaryReportRequest(
    Guid CompanyId,
    int? PolicyYear = null,
    Guid? DepartmentId = null,
    LeaveSummaryGroupBy GroupBy = LeaveSummaryGroupBy.Employee);
