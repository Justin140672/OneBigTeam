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

    public string? SourceReference { get; init; }

    /// <summary>
    /// Starting compensation, created in the same transaction as the employee and effective from
    /// <see cref="StartDate"/>. Mandatory on the create-employee endpoint (see the validator);
    /// internal provisioning paths (accepted candidate offer, company sign-up) may omit it.
    /// <see cref="SalaryFrequency"/> is "Annual" | "Hourly" | "Daily" (defaults to Annual) and
    /// <see cref="Currency"/> is a 3-letter ISO 4217 code (defaults to GBP).
    /// </summary>
    public decimal? Salary { get; init; }
    public string? SalaryFrequency { get; init; }
    public string? Currency { get; init; }

    public bool IsInitialCompanyAdmin { get; init; }

    // Populated by the endpoint from the optional "Idempotency-Key" request header (ticket 3, P1
    // follow-up). Null when the caller didn't supply one, in which case no dedup is attempted.
    internal string? IdempotencyKey { get; init; }

    internal Guid? ActorEmployeeId { get; init; }
}
