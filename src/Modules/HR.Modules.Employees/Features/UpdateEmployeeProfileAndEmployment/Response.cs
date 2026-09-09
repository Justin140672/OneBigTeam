using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Contracts;
using HR.Modules.Employees.Domain;

namespace HR.Modules.Employees.Features.UpdateEmployeeProfileAndEmployment;

internal sealed record UpdateEmployeeProfileAndEmploymentResponse(
    Guid Id,
    Guid CompanyId,
    string FirstName,
    string LastName,
    string WorkEmail,
    string? PersonalEmail,
    string? EmployeeNumber,
    Guid? EmploymentTypeId,
    EmploymentStatus Status,
    Guid? DepartmentId,
    Guid? LocationId,
    Guid? PositionProfileId,
    Guid? ManagerId,
    DateOnly StartDate,
    DateOnly? ContinuousServiceDate,
    DateOnly? ProbationEndDate,
    DateOnly? LeavingDate,
    NoticePeriodUnit? NoticePeriodUnitOverride,
    int? NoticePeriodLengthOverride,
    WorkingDays? WorkingDaysOverride,
    decimal? HoursPerDayOverride,
    string? Notes,
    bool HasSystemAccess,
    DateTimeOffset UpdatedAt,
    int Version);
