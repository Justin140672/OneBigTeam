namespace HR.Modules.Employees.Features.CreateEmployee;

internal sealed record CreateEmployeeRequest
{
    public Guid? Id { get; init; }
    public Guid CompanyId { get; init; }
    public Guid DepartmentId { get; init; }
    public Guid LocationId { get; init; }
    public Guid PositionProfileId { get; init; }
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
    public string EmployeeNumber { get; init; } = string.Empty;
    public Guid EmploymentTypeId { get; init; }
    public string? PhoneNumber { get; init; }
    public string? HomePhone { get; init; }
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? City { get; init; }
    public string? County { get; init; }
    public string? PostCode { get; init; }
    public string? Country { get; init; }
    public bool HasSystemAccess { get; init; } = true;

    /// <summary>
    /// NFR-08: optional idempotency key set by automated provisioning flows (e.g. candidate hire).
    /// When supplied and an employee with the same (CompanyId, SourceReference) already exists, the
    /// handler returns that employee instead of creating a duplicate and does not re-publish
    /// EmployeeCreated. Null for human-initiated creation.
    /// </summary>
    public string? SourceReference { get; init; }
}
