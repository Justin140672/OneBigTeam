using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.GetLeaveSummaryReport;

namespace HR.Modules.Reporting.Features.ExportLeaveSummaryReport;

internal sealed record ExportLeaveSummaryReportRequest(
    Guid CompanyId,
    int? PolicyYear,
    Guid? DepartmentId,
    LeaveSummaryGroupBy GroupBy = LeaveSummaryGroupBy.Employee,
    Guid? LeaveTypeId = null,
    ReportExportFormat Format = ReportExportFormat.Csv);
