using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Features.ExportGovernanceSecurityEventsReport;

internal sealed record ExportGovernanceSecurityEventsReportResponse(ReportExportFile File, int TotalCount, bool IsTruncated);
