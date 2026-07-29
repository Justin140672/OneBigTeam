using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Features.ExportDocumentComplianceReport;

internal sealed record ExportDocumentComplianceReportRequest(
    Guid CompanyId,
    Guid? PositionProfileId,
    ReportExportFormat Format = ReportExportFormat.Csv);
