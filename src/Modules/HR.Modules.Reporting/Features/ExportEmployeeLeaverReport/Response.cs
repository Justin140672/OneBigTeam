using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Features.ExportEmployeeLeaverReport;

internal sealed record ExportEmployeeLeaverReportResponse(ReportExportFile File, int TotalCount, bool IsTruncated);
