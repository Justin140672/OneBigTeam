using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Contracts;
using HR.Modules.Employees.Domain;

namespace HR.Modules.Employees.Features.UpdateEmployeeProfileAndEmployment;

// Ticket 2 (item 5): a single transactional save for the Employee Edit screen, which previously
// called UpdateEmployeeProfile then UpdateEmploymentDetails sequentially. Because employment
// details live on the same Employee aggregate row, both mutations are applied to one tracked
// entity and committed with a single optimistic-concurrency guarded SaveChanges — so a conflict
// on either half rolls back the whole save and the banner/version stay accurate. The standalone
// UpdateEmploymentDetails endpoint is kept for screens that only edit the employment tab.
internal sealed record UpdateEmployeeProfileAndEmploymentRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }

    // Profile
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string? PreferredName { get; init; }
    public string WorkEmail { get; init; } = string.Empty;
    public string? PersonalEmail { get; init; }
    public DateOnly? DateOfBirth { get; init; }
    public string? Nationality { get; init; }
    public string? Gender { get; init; }
    public string? GenderOther { get; init; }
    public string? PhoneNumber { get; init; }
    public string? HomePhone { get; init; }
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? City { get; init; }
    public string? County { get; init; }
    public string? PostCode { get; init; }
    public string? Country { get; init; }
    public bool HasSystemAccess { get; init; } = true;

    // Employment (authoritative for the fields shared with the profile tab)
    public string? EmployeeNumber { get; init; }
    public Guid? EmploymentTypeId { get; init; }
    public EmploymentStatus Status { get; init; }
    public Guid? DepartmentId { get; init; }
    public Guid? LocationId { get; init; }
    public Guid? PositionProfileId { get; init; }
    public Guid? ManagerId { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly? ContinuousServiceDate { get; init; }
    public DateOnly? ProbationEndDate { get; init; }
    public DateOnly? LeavingDate { get; init; }
    public NoticePeriodUnit? NoticePeriodUnitOverride { get; init; }
    public int? NoticePeriodLengthOverride { get; init; }
    public WorkingDays? WorkingDaysOverride { get; init; }
    public decimal? HoursPerDayOverride { get; init; }
    public string? Notes { get; init; }

    // One correlation id for the merged audit entry, one version guarding the whole aggregate.
    public Guid? CorrelationId { get; init; }
    public int? ExpectedVersion { get; init; }
}
