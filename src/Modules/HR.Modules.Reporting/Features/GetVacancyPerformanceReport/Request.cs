namespace HR.Modules.Reporting.Features.GetVacancyPerformanceReport;

internal sealed record GetVacancyPerformanceReportRequest(
    Guid CompanyId,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null);
