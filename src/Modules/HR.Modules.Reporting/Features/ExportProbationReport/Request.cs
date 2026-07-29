using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Features.ExportProbationReport;

internal sealed record ExportProbationReportRequest(
    Guid CompanyId,
    ReportExportFormat Format = ReportExportFormat.Csv);
