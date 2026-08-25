using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Features.ExportEmployeeStarterReport;

internal sealed record ExportEmployeeStarterReportResponse(ReportExportFile File, int TotalCount, bool IsTruncated);
