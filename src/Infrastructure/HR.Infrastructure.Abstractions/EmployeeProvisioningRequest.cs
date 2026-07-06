namespace HR.Infrastructure.Abstractions;

public sealed record EmployeeProvisioningRequest(
    Guid CompanyId,
    string FirstName,
    string LastName,
    string WorkEmail,
    DateOnly StartDate,
    DateOnly DateOfBirth,
    string Nationality,
    string Gender,
    string? GenderOther = null,
    string? PersonalEmail = null,
    string? PhoneNumber = null,
    Guid? DepartmentId = null,
    Guid? PositionProfileId = null,
    Guid? ManagerId = null);
