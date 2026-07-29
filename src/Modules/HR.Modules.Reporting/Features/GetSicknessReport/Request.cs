namespace HR.Modules.Reporting.Features.GetSicknessReport;

internal enum SicknessReportGroupBy
{
    Employee = 1,
    Department = 2,
}

internal sealed record GetSicknessReportRequest(
    Guid CompanyId,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    SicknessReportGroupBy GroupBy = SicknessReportGroupBy.Employee);
