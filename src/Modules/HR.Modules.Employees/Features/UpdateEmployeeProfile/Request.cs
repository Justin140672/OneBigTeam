namespace HR.Modules.Employees.Features.UpdateEmployeeProfile;

internal sealed record UpdateEmployeeProfileRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }
    public Guid? DepartmentId { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string WorkEmail { get; init; } = string.Empty;
    public string? PersonalEmail { get; init; }
    public DateOnly StartDate { get; init; }
    public bool HasSystemAccess { get; init; } = true;
}
