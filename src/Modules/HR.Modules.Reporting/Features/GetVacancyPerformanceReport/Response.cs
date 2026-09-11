namespace HR.Modules.Reporting.Features.GetVacancyPerformanceReport;

internal sealed record GetVacancyPerformanceReportResponse(IReadOnlyList<VacancyPerformanceReportRow> Items);

internal sealed record VacancyPerformanceReportRow(
    Guid VacancyId,
    string VacancyTitle,
    int DaysOpen,
    int CandidateCount,
    int InterviewCount,
    int OfferCount,
    DateOnly? HireDate);
