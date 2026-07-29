using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Features.ExportCompanyDocumentAcknowledgementReport;

internal sealed record ExportCompanyDocumentAcknowledgementReportRequest(
    Guid CompanyId,
    ReportExportFormat Format = ReportExportFormat.Csv);
