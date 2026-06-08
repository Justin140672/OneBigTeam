using HR.Modules.Employees.Domain;

namespace HR.Modules.Employees.Features.UpdateEmployeeProfile;

internal sealed record UpdateEmployeeProfileResponse(
    Guid Id,
    Guid CompanyId,
    string FirstName,
    string LastName,
    string WorkEmail,
    string? PersonalEmail,
    DateOnly StartDate,
    EmploymentStatus Status,
    DateTimeOffset UpdatedAt);
