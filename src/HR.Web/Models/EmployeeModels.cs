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
    DateTimeOffset CreatedAt,
    string? ProfilePhotoUrl)
{
    public string FullName => $"{FirstName} {LastName}";
}

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
    int DirectReportsCount,
    IReadOnlyList<ReportingChainItemModel> ReportingChain,
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
    NoticePeriodUnit? NoticePeriodUnitOverride,
    int? NoticePeriodLengthOverride,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool ShowOnboardingTab,
    bool ShowProbationTab,
    bool ShowOffboardingTab,
    bool ShowLeavingTab,
    NoticePeriodUnit EffectiveNoticePeriodUnit,
    int EffectiveNoticePeriodLength,
    string EffectiveNoticePeriodSource);

// Ordered from the top of the org down to the employee's immediate manager; does not include
// the employee themselves.
public sealed record ReportingChainItemModel(Guid EmployeeId, string Name, string? JobTitle);

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
    private string _postCode = string.Empty;
    [DynamicRegex(nameof(PostcodeRegexPattern), ErrorMessage = "Enter a valid postcode.")]
    public string PostCode
    {
        get => _postCode;
        set => _postCode = value.ToUpperInvariant();
    }
    public string Country { get; set; } = string.Empty;

    // Populated from the company's settings after load; not bound to any input — used only as
    // the pattern source for the [DynamicRegex] attributes above.
    public string? PostcodeRegexPattern { get; set; }
    public string? TelephoneRegexPattern { get; set; }
    public string? MobileRegexPattern { get; set; }
    [RequiredUnless(nameof(EmployeeNumberAutoAssigned), ErrorMessage = "Employee number is required.")]
    public string EmployeeNumber { get; set; } = string.Empty;

    // Set by EmployeeEdit for a brand-new employee when the company's numbering mode is
    // Automatic — the Employee Number field is hidden in that case and this flag lets
    // RequiredUnless skip validation instead of blocking Save with an empty required field.
    public bool EmployeeNumberAutoAssigned { get; set; }
    [Required(ErrorMessage = "Employment type is required.")]
    public Guid? EmploymentTypeId { get; set; }
    [Required(ErrorMessage = "Department is required.")]
    public Guid? DepartmentId { get; set; }
    [Required(ErrorMessage = "Location is required.")]
    public Guid? LocationId { get; set; }
    [Required(ErrorMessage = "Position profile is required.")]
    public Guid? PositionProfileId { get; set; }
    public Guid? ManagerId { get; set; }
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
    Guid DepartmentId,
    Guid LocationId,
    Guid PositionProfileId,
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
    string EmployeeNumber,
    Guid EmploymentTypeId,
    string? PhoneNumber,
    string? HomePhone,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? County,
    string? PostCode,
    string? Country,
    bool HasSystemAccess,
    Guid? ManagerId = null);

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
    string? Notes,
    NoticePeriodUnit? NoticePeriodUnitOverride = null,
    int? NoticePeriodLengthOverride = null);

// ── LEAVING PROCESS ────────────────────────────────────────────────────────────

public sealed record StartLeavingProcessRequest(
    Guid CompanyId,
    Guid EmployeeId,
    DateOnly ResignationReceivedDate,
    DateOnly LeavingDate,
    DateOnly LastWorkingDay,
    string LeavingReason,
    bool ConfirmBackdatedLeavingDate = false);

public sealed record StartLeavingProcessResponse(
    Guid Id,
    Guid CompanyId,
    Guid EmployeeId,
    DateOnly ResignationReceivedDate,
    DateOnly LeavingDate,
    DateOnly LastWorkingDay,
    NoticePeriodUnit NoticePeriodUnit,
    int NoticePeriodLength,
    string NoticeSource,
    string LeavingReason,
    string Status,
    DateTimeOffset StartedAt);

public sealed record LeavingProcessResponse(
    Guid Id,
    DateOnly ResignationReceivedDate,
    DateOnly LeavingDate,
    DateOnly LastWorkingDay,
    NoticePeriodUnit NoticePeriodUnit,
    int NoticePeriodLength,
    string NoticeSource,
    string LeavingReason,
    string Status);

public sealed record AmendLeavingProcessRequest(
    Guid CompanyId,
    Guid EmployeeId,
    DateOnly LeavingDate,
    DateOnly LastWorkingDay,
    string LeavingReason,
    bool ConfirmBackdatedLeavingDate = false);

public sealed record AmendLeavingProcessResponse(
    Guid Id,
    Guid CompanyId,
    Guid EmployeeId,
    DateOnly ResignationReceivedDate,
    DateOnly LeavingDate,
    DateOnly LastWorkingDay,
    NoticePeriodUnit NoticePeriodUnit,
    int NoticePeriodLength,
    string NoticeSource,
    string LeavingReason,
    string Status,
    bool OffboardingAlreadyStarted);

public sealed record CancelLeavingProcessRequest(
    Guid CompanyId,
    Guid EmployeeId,
    string CancellationReason);

public sealed record CancelLeavingProcessResponse(
    Guid Id,
    Guid CompanyId,
    Guid EmployeeId,
    string Status,
    bool OffboardingTasksCancelled);

// ── NATIONALITIES ─────────────────────────────────────────────────────────────

public record ListNationalitiesResponse(IReadOnlyList<NationalityListItem> Items);

public record NationalityListItem(int Id, string Name);

// ── DASHBOARD: HEADCOUNT SUMMARY ────────────────────────────────────────────────

public sealed record GetHeadcountSummaryResponse(IReadOnlyList<HeadcountSummaryItem> Items);

public sealed record HeadcountSummaryItem(
    Guid? DepartmentId,
    string DepartmentName,
    int EmployeeCount);

// ── DASHBOARD: NEW HIRES TREND ──────────────────────────────────────────────────

public sealed record GetNewHiresTrendResponse(IReadOnlyList<NewHiresTrendItem> Items);

public sealed record NewHiresTrendItem(
    int Year,
    int Month,
    string MonthLabel,
    int NewHireCount);

// ── DASHBOARD: RECENT EMPLOYEE CHANGES ──────────────────────────────────────────

public sealed record GetRecentEmployeeChangesResponse(IReadOnlyList<RecentEmployeeChangeItem> Items);

public sealed record RecentEmployeeChangeItem(
    DateTimeOffset OccurredAt,
    string EmployeeName,
    string Action,
    string ActorName);

// ── DASHBOARD: MY TEAM ──────────────────────────────────────────────────────────

public sealed record GetMyTeamResponse(IReadOnlyList<TeamMemberItem> Items);

public sealed record TeamMemberItem(
    Guid EmployeeId,
    string FullName,
    string? JobTitle,
    string? PhoneNumber,
    string WorkEmail,
    string? ProfilePhotoUrl);

