using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Features.ExportLeaveCalendarReport;

internal sealed record ExportLeaveCalendarReportRequest(
    Guid CompanyId,
    int Year,
    int Month,
    Guid? DepartmentId = null,
    ReportExportFormat Format = ReportExportFormat.Csv);
