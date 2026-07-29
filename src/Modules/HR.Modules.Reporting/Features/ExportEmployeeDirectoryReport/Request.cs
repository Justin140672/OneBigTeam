using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Features.ExportEmployeeDirectoryReport;

internal sealed record ExportEmployeeDirectoryReportRequest(
    Guid CompanyId,
    Guid? DepartmentId,
    Guid? LocationId,
    Guid? PositionProfileId,
    Guid? ManagerId,
    Guid? EmploymentTypeId,
    DateOnly? DateRangeStart,
    DateOnly? DateRangeEnd,
    string? EmployeeStatus,
    string? SortBy = null,
    bool SortDescending = false,
    ReportExportFormat Format = ReportExportFormat.Csv);
