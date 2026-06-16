namespace HR.Modules.Employees.Features.CreateEmployee;

internal sealed record CreateEmployeeRequest
{
    public Guid CompanyId { get; init; }
    public Guid? DepartmentId { get; init; }
    public Guid? PositionProfileId { get; init; }
    public Guid? ManagerId { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string? PreferredName { get; init; }
    public string WorkEmail { get; init; } = string.Empty;
    public string? PersonalEmail { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly DateOfBirth { get; init; }
    public string Nationality { get; init; } = string.Empty;
    public string Gender { get; init; } = string.Empty;
    public string? GenderOther { get; init; }
    public bool HasSystemAccess { get; init; } = true;
}
