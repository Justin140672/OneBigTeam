using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Features.ExportGovernanceUserActivityReport;

internal sealed record ExportGovernanceUserActivityReportResponse(ReportExportFile File, int TotalCount, bool IsTruncated);
