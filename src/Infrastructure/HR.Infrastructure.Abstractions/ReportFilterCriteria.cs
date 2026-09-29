namespace HR.Infrastructure.Abstractions;

public sealed record ReportFilterCriteria(
    Guid? CompanyId = null,
    Guid? DepartmentId = null,
    Guid? LocationId = null,
    Guid? PositionProfileId = null,
    Guid? ManagerId = null,
    Guid? EmploymentTypeId = null,
    DateOnly? DateRangeStart = null,
    DateOnly? DateRangeEnd = null,
    string? EmployeeStatus = null,
    string? RecruitmentStatus = null);
