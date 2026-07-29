namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Shared, cross-module filter criteria reused by every report handler in the Reporting module.
/// All fields are optional so callers only supply the filters relevant to a given report.
/// </summary>
/// <param name="CompanyId">
/// Optional company filter supplied by the caller. Tenant scoping is normally resolved
/// server-side via ICurrentTenant/the caller's own company claim, never from client input.
/// This field must never be used to override the server-resolved tenant context — it may only
/// narrow a report further within the caller's own company.
/// </param>
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
