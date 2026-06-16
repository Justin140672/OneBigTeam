using HR.Modules.Employees.Domain;
using HR.SharedKernel;

namespace HR.Modules.Employees.Features.GetEmployee;

internal sealed record GetEmployeeResponse(
    Guid Id,
    Guid CompanyId,
    Guid? DepartmentId,
    string? DepartmentName,
    Guid? PositionProfileId,
    string? PositionTitle,
    Guid? ManagerId,
    string? ManagerFullName,
    string FirstName,
    string LastName,
    string? PreferredName,
    string WorkEmail,
    string? PersonalEmail,
    DateOnly StartDate,
    DateOnly? DateOfBirth,
    string? Nationality,
    string? Gender,
    string? GenderOther,
    EmploymentStatus Status,
    bool HasSystemAccess,
    WorkingDays? WorkingDaysOverride,
    decimal? HoursPerDayOverride,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
