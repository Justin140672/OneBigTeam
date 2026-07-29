using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Features.ExportOnboardingProgressReport;

internal sealed record ExportOnboardingProgressReportRequest(
    Guid CompanyId,
    bool OverdueOnly = false,
    ReportExportFormat Format = ReportExportFormat.Csv);
