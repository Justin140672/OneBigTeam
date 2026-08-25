using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Features.ExportWorkloadActions;

internal sealed record ExportWorkloadActionsResponse(ReportExportFile File, int TotalCount, bool IsTruncated);
