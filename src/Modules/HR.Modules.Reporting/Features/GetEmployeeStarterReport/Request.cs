namespace HR.Modules.Reporting.Features.GetEmployeeStarterReport;

internal sealed record GetEmployeeStarterReportRequest(
    Guid CompanyId,
    Guid? DepartmentId,
    Guid? LocationId,
    Guid? PositionProfileId,
    Guid? EmploymentTypeId,
    DateOnly? DateRangeStart,
    DateOnly? DateRangeEnd,
    int Page = 1,
    int PageSize = 20,
    string? SortBy = null,
    bool SortDescending = false);
