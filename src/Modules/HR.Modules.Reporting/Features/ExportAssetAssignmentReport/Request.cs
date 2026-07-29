using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Features.ExportAssetAssignmentReport;

internal sealed record ExportAssetAssignmentReportRequest(
    Guid CompanyId,
    ReportExportFormat Format = ReportExportFormat.Csv);
