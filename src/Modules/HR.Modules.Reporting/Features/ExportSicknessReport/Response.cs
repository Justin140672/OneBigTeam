using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Features.ExportSicknessReport;

internal sealed record ExportSicknessReportResponse(ReportExportFile File, int TotalCount, bool IsTruncated);
