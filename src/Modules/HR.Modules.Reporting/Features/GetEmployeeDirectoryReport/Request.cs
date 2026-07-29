namespace HR.Modules.Reporting.Features.GetEmployeeDirectoryReport;

internal sealed record GetEmployeeDirectoryReportRequest(
    Guid CompanyId,
    Guid? DepartmentId,
    Guid? LocationId,
    Guid? PositionProfileId,
    Guid? ManagerId,
    Guid? EmploymentTypeId,
    DateOnly? DateRangeStart,
    DateOnly? DateRangeEnd,
    string? EmployeeStatus,
    int Page = 1,
    int PageSize = 20,
    string? SortBy = null,
    bool SortDescending = false);
