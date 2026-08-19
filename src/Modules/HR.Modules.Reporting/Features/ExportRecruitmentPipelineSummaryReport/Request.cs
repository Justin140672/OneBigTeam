using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Features.ExportRecruitmentPipelineSummaryReport;

internal sealed record ExportRecruitmentPipelineSummaryReportRequest(
    Guid CompanyId,
    bool IncludeClosed = false,
    ReportExportFormat Format = ReportExportFormat.Csv);
