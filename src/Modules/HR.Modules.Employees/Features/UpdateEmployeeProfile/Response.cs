using HR.Modules.Employees.Domain;

namespace HR.Modules.Employees.Features.UpdateEmployeeProfile;

internal sealed record UpdateEmployeeProfileResponse(
    Guid Id,
    Guid CompanyId,
    Guid? DepartmentId,
    string FirstName,
    string LastName,
    string WorkEmail,
    string? PersonalEmail,
    DateOnly StartDate,
    EmploymentStatus Status,
    bool HasSystemAccess,
    DateTimeOffset UpdatedAt);
