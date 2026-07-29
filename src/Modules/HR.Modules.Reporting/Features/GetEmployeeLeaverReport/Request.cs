namespace HR.Modules.Reporting.Features.GetEmployeeLeaverReport;

internal sealed record GetEmployeeLeaverReportRequest(
    Guid CompanyId,
    Guid? DepartmentId,
    Guid? PositionProfileId,
    DateOnly? DateRangeStart,
    DateOnly? DateRangeEnd,
    int Page = 1,
    int PageSize = 20,
    string? SortBy = null,
    bool SortDescending = false);
