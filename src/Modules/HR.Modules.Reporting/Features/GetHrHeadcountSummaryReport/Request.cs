namespace HR.Modules.Reporting.Features.GetHrHeadcountSummaryReport;

internal sealed record GetHrHeadcountSummaryReportRequest(
    Guid CompanyId,
    Guid? DepartmentId,
    Guid? LocationId,
    Guid? EmploymentTypeId,
    string? EmployeeStatus);
