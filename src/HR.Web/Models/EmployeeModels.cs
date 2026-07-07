using System.ComponentModel.DataAnnotations;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Web.Models;

// ── LIST ──────────────────────────────────────────────────────────────────────

public record ListEmployeesResponse(
    List<EmployeeListItemModel> Items,
    int TotalCount,
    int PageNumber,
    int PageSize,
    int TotalPages);

public record EmployeeListItemModel(
    Guid Id,
    Guid CompanyId,
    Guid? DepartmentId,
    string? DepartmentName,
    Guid? LocationId,
    string? LocationName,
    Guid? PositionProfileId,
    string? PositionProfileTitle,
    Guid? ManagerId,
    string? ManagerFullName,
    string FirstName,
    string LastName,
    string WorkEmail,
    DateOnly StartDate,
    string Status,
    DateTimeOffset CreatedAt);

// ── GET ───────────────────────────────────────────────────────────────────────

public record GetEmployeeResponse(
    Guid Id,
    Guid CompanyId,
    Guid? DepartmentId,
    string? DepartmentName,
    Guid? LocationId,
    string? LocationName,
    Guid? PositionProfileId,
    string? PositionTitle,
    Guid? ManagerId,
    string? ManagerFullName,
    string FirstName,
    string LastName,
    string? PreferredName,
    string WorkEmail,
    string? PersonalEmail,
    DateOnly StartDate,
    DateOnly? DateOfBirth,
    string? Nationality,
    string? Gender,
    string? GenderOther,
    string? PhoneNumber,
    string? HomePhone,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? County,
    string? PostCode,
    string? Country,
    string Status,
    bool HasSystemAccess,
    WorkingDays? WorkingDaysOverride,
    decimal? HoursPerDayOverride,
    string? EmployeeNumber,
    Guid? EmploymentTypeId,
    string? EmploymentTypeName,
    DateOnly? ContinuousServiceDate,
    DateOnly? ProbationEndDate,
    DateOnly? LeavingDate,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

// ── PERSONAL DETAILS ──────────────────────────────────────────────────────────

public sealed record GetMyPersonalDetailsResponse(
    Guid EmployeeId,
    string FirstName,
    string LastName,
    string? PreferredName,
    DateOnly? DateOfBirth,
    string? Nationality,
    string? Gender);

public sealed record RequestPersonalDetailsChangeRequest(string Notes);

public sealed record RequestPersonalDetailsChangeResponse(Guid TaskId);

// ── EDIT MODELS ───────────────────────────────────────────────────────────────

public sealed class EmployeeProfileEditModel
{
    [Required(ErrorMessage = "First name is required.")]
    public string FirstName { get; set; } = string.Empty;
    [Required(ErrorMessage = "Last name is required.")]
    public string LastName { get; set; } = string.Empty;
    public string PreferredName { get; set; } = string.Empty;
    [Required(ErrorMessage = "Work email is required.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    public string WorkEmail { get; set; } = string.Empty;
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    public string? PersonalEmail { get; set; }
    public DateOnly StartDate { get; set; }
    [Required(ErrorMessage = "Date of birth is required.")]
    public DateOnly? DateOfBirth { get; set; }
    [Required(ErrorMessage = "Nationality is required.")]
    public string Nationality { get; set; } = string.Empty;
    [Required(ErrorMessage = "Gender is required.")]
    public string Gender { get; set; } = string.Empty;
    public string GenderOther { get; set; } = string.Empty;
    [DynamicRegex(nameof(MobileRegexPattern), ErrorMessage = "Enter a valid mobile number.")]
    public string PhoneNumber { get; set; } = string.Empty;
    [DynamicRegex(nameof(TelephoneRegexPattern), ErrorMessage = "Enter a valid phone number.")]
    public string HomePhone { get; set; } = string.Empty;
    public string AddressLine1 { get; set; } = string.Empty;
    public string AddressLine2 { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string County { get; set; } = string.Empty;
    [DynamicRegex(nameof(PostcodeRegexPattern), ErrorMessage = "Enter a valid postcode.")]
    public string PostCode { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;

    // Populated from the company's settings after load; not bound to any input — used only as
    // the pattern source for the [DynamicRegex] attributes above.
    public string? PostcodeRegexPattern { get; set; }
    public string? TelephoneRegexPattern { get; set; }
    public string? MobileRegexPattern { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? LocationId { get; set; }
    public Guid? PositionProfileId { get; set; }
    public bool HasSystemAccess { get; set; } = true;
    public bool OverrideWorkingPattern { get; set; } = false;
    public HashSet<string> WorkingWeek { get; set; } = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"];
    public decimal HoursPerDay { get; set; } = 7.5m;
}

public record UpdateEmployeeProfileRequest(
    Guid CompanyId,
    Guid Id,
    Guid? DepartmentId,
    Guid? LocationId,
    Guid? PositionProfileId,
    string FirstName,
    string LastName,
    string? PreferredName,
    string WorkEmail,
    string? PersonalEmail,
    DateOnly StartDate,
    DateOnly? DateOfBirth,
    string? Nationality,
    string? Gender,
    string? GenderOther,
    string? PhoneNumber,
    string? HomePhone,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? County,
    string? PostCode,
    string? Country,
    bool HasSystemAccess,
    WorkingDays? WorkingDaysOverride,
    decimal? HoursPerDayOverride);

public record UpdateEmployeeProfileResponse(
    Guid Id,
    Guid CompanyId,
    Guid? DepartmentId,
    Guid? LocationId,
    string FirstName,
    string LastName,
    string WorkEmail,
    string? PersonalEmail,
    DateOnly StartDate,
    string Status,
    DateTimeOffset UpdatedAt);

// ── CREATE ────────────────────────────────────────────────────────────────────

public sealed class CreateEmployeeFormModel
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string WorkEmail { get; set; } = string.Empty;
    public string? PersonalEmail { get; set; }
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
}

public record CreateEmployeeRequest(
    Guid CompanyId,
    Guid? DepartmentId,
    Guid? LocationId,
    Guid? PositionProfileId,
    string FirstName,
    string LastName,
    string? PreferredName,
    string WorkEmail,
    string? PersonalEmail,
    DateOnly StartDate,
    DateOnly DateOfBirth,
    string Nationality,
    string Gender,
    string? GenderOther,
    string? PhoneNumber,
    string? HomePhone,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? County,
    string? PostCode,
    string? Country,
    bool HasSystemAccess);

public record CreateEmployeeResponse(
    Guid Id,
    Guid CompanyId,
    string FirstName,
    string LastName,
    string WorkEmail,
    string Status,
    DateTimeOffset CreatedAt);

// ── CONTACT DETAILS ───────────────────────────────────────────────────────────

public sealed record GetMyContactDetailsResponse(
    string WorkEmail,
    string? PersonalEmail,
    string? PhoneNumber,
    string? HomePhone,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? County,
    string? PostCode,
    string? Country);

public sealed record UpdateMyContactDetailsRequest(
    Guid CompanyId,
    string? PersonalEmail,
    string? PhoneNumber,
    string? HomePhone,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string? County,
    string PostCode,
    string Country);

// ── EMERGENCY CONTACTS ────────────────────────────────────────────────────────

public sealed record EmergencyContactItem(
    Guid Id,
    string Name,
    string Relationship,
    string PhoneNumber,
    string? Email);

public sealed record GetEmergencyContactsResponse(List<EmergencyContactItem> Contacts);

public sealed record AddEmergencyContactRequest(
    Guid CompanyId,
    string Name,
    string Relationship,
    string PhoneNumber,
    string? Email);

public sealed record UpdateEmergencyContactRequest(
    Guid CompanyId,
    Guid ContactId,
    string Name,
    string Relationship,
    string PhoneNumber,
    string? Email);

// ── EMPLOYMENT DETAILS ────────────────────────────────────────────────────────

public record UpdateEmploymentDetailsRequest(
    Guid CompanyId,
    Guid Id,
    string? EmployeeNumber,
    Guid? EmploymentTypeId,
    string Status,
    Guid? DepartmentId,
    Guid? LocationId,
    Guid? PositionProfileId,
    Guid? ManagerId,
    DateOnly StartDate,
    DateOnly? ContinuousServiceDate,
    DateOnly? ProbationEndDate,
    DateOnly? LeavingDate,
    WorkingDays? WorkingDaysOverride,
    decimal? HoursPerDayOverride,
    string? Notes);

// ── NATIONALITIES ─────────────────────────────────────────────────────────────

public record ListNationalitiesResponse(IReadOnlyList<NationalityListItem> Items);

public record NationalityListItem(int Id, string Name);

