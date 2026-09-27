namespace HR.Modules.Reporting.Features.GetVacancyPerformanceReport;

internal sealed record GetVacancyPerformanceReportRequest(
    Guid CompanyId,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    // Internal recruitment Ticket 6: null = all applications, true = internal only, false = external only.
    bool? IsInternal = null);
