using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Features.ExportGovernanceComplianceStatusReport;

internal sealed record ExportGovernanceComplianceStatusReportResponse(ReportExportFile File, int TotalCount, bool IsTruncated);
