using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Features.ExportEmployeeStarterReport;

internal sealed record ExportEmployeeStarterReportRequest(
    Guid CompanyId,
    Guid? DepartmentId,
    Guid? LocationId,
    Guid? PositionProfileId,
    Guid? EmploymentTypeId,
    DateOnly? DateRangeStart,
    DateOnly? DateRangeEnd,
    string? SortBy = null,
    bool SortDescending = false,
    ReportExportFormat Format = ReportExportFormat.Csv);
