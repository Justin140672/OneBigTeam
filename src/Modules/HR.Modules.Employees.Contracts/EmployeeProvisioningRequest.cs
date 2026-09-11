namespace HR.Modules.Employees.Contracts;

public sealed record EmployeeProvisioningRequest(
    Guid CompanyId,
    string FirstName,
    string LastName,
    string WorkEmail,
    DateOnly StartDate,
    DateOnly DateOfBirth,
    string Nationality,
    string Gender,
    string EmployeeNumber,
    Guid EmploymentTypeId,
    Guid DepartmentId,
    Guid LocationId,
    Guid PositionProfileId,
    string? GenderOther = null,
    string? PersonalEmail = null,
    string? PhoneNumber = null,
    Guid? ManagerId = null,
    string? AddressLine1 = null,
    string? AddressLine2 = null,
    string? City = null,
    string? County = null,
    string? PostCode = null,
    /// <summary>
    /// NFR-08: stable idempotency key for this provisioning. When the calling workflow is retried
    /// after a partial failure, supplying the same value guarantees the same employee is returned
    /// instead of a duplicate being created. Format: "&lt;source&gt;:&lt;entity&gt;:&lt;id&gt;".
    /// </summary>
    string? SourceReference = null,
    /// <summary>
    /// Ticket 2: agreed compensation for an automated hire (candidate offer accepted). When
    /// <paramref name="Salary"/> is a positive value, the Employees module seeds the new hire's first
    /// Compensation record from it so HR does not re-enter what was agreed on the offer.
    /// <paramref name="SalaryFrequency"/> is "Annual" | "Hourly" | "Daily" (defaults to Annual when
    /// unrecognised). Both null for human-initiated creation, which manages compensation separately.
    /// </summary>
    decimal? Salary = null,
    string? SalaryFrequency = null);
