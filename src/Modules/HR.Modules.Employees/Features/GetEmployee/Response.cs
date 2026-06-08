using HR.Modules.Employees.Domain;

namespace HR.Modules.Employees.Features.GetEmployee;

internal sealed record GetEmployeeResponse(
    Guid Id,
    Guid CompanyId,
    Guid? DepartmentId,
    Guid? PositionProfileId,
    Guid? ManagerId,
    string FirstName,
    string LastName,
    string WorkEmail,
    string? PersonalEmail,
    DateOnly StartDate,
    EmploymentStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
