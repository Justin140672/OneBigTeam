using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Features.ExportHrHeadcountSummaryReport;

internal sealed record ExportHrHeadcountSummaryReportRequest(
    Guid CompanyId,
    Guid? DepartmentId,
    Guid? LocationId,
    Guid? EmploymentTypeId,
    string? EmployeeStatus,
    ReportExportFormat Format = ReportExportFormat.Csv);
