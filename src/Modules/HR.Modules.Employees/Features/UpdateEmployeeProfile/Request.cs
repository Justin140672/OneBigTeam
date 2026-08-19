using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Employees.Features.UpdateEmployeeProfile;

internal sealed record UpdateEmployeeProfileRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }
    public Guid? DepartmentId { get; init; }
    public Guid? LocationId { get; init; }
    public Guid? PositionProfileId { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string? PreferredName { get; init; }
    public string WorkEmail { get; init; } = string.Empty;
    public string? PersonalEmail { get; init; }
    public DateOnly StartDate { get; init; }
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
    public WorkingDays? WorkingDaysOverride { get; init; }
    public decimal? HoursPerDayOverride { get; init; }

    // Ticket: "merge Employee + Employment tab audit entries when saved together". Optional —
    // defaults to null so existing callers of this request (e.g. any other page that only edits
    // the Employee Profile tab in isolation) are unaffected. EmployeeEdit.razor's combined save
    // generates one Guid and passes it into both this request and UpdateEmploymentDetailsRequest
    // so GetEmployeeAuditHistoryHandler can merge the two resulting audit rows into a single item.
    public Guid? CorrelationId { get; init; }
}
