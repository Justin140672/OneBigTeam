using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Features.ExportGovernanceAdministrativeChangesReport;

internal sealed record ExportGovernanceAdministrativeChangesReportResponse(ReportExportFile File, int TotalCount, bool IsTruncated);
