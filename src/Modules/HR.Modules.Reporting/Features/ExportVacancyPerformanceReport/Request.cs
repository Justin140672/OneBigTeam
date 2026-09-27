using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Features.ExportVacancyPerformanceReport;

internal sealed record ExportVacancyPerformanceReportRequest(
    Guid CompanyId,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    ReportExportFormat Format = ReportExportFormat.Csv,
    // Internal recruitment Ticket 6: null = all applications, true = internal only, false = external only.
    bool? IsInternal = null);
