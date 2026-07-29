using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Features.ExportOffboardingProgressReport;

internal sealed record ExportOffboardingProgressReportRequest(
    Guid CompanyId,
    ReportExportFormat Format = ReportExportFormat.Csv);
