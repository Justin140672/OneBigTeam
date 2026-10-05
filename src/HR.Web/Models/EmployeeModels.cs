using System.ComponentModel.DataAnnotations;
using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Web.Models;


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
    string? EmployeeNumber,
    DateOnly StartDate,
    string Status,
    DateTimeOffset CreatedAt,
    string? ProfilePhotoUrl,
    string UserAccountStatus,
    string? PreferredName = null)
{
    public string FullName => HR.SharedKernel.PersonName.Display(FirstName, LastName, PreferredName);
    public string LegalName => HR.SharedKernel.PersonName.Legal(FirstName, LastName);
    public bool HasDistinctPreferredName => HR.SharedKernel.PersonName.IsPreferredDistinct(FirstName, PreferredName);
}


public record EmployeeDirectorySearchResponse(IReadOnlyList<EmployeeDirectorySearchItem> Items);

public record EmployeeDirectorySearchItem(
    Guid Id,
    string FirstName,
    string LastName,
    string? EmployeeNumber,
    string? PositionProfileTitle,
    string? DepartmentName,
    string Status,
    string? PreferredName = null)
{
    public string FullName => HR.SharedKernel.PersonName.Display(FirstName, LastName, PreferredName);
}


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
    bool CanStartLeavingProcess,
    NoticePeriodUnit EffectiveNoticePeriodUnit,
    int EffectiveNoticePeriodLength,
    string EffectiveNoticePeriodSource,
    // Ticket 2: optimistic-concurrency token echoed back on the next employee edit save.
    int Version = 0)
{
    public string FullName => HR.SharedKernel.PersonName.Display(FirstName, LastName, PreferredName);
}


public sealed record ReportingChainItemModel(Guid EmployeeId, string Name, string? JobTitle);

// ── GET (manager team-view: operational fields only) ────────────────────────────
// Deliberately a distinct model from GetEmployeeResponse above, not a shared type with unused
// properties — mirrors HR.Modules.Employees.Features.GetEmployeeTeamView.GetEmployeeTeamViewResponse
// field-for-field. Deserializing the manager-scope API response into the full GetEmployeeResponse
// model would silently leave every restricted field at its default value instead of surfacing
// that this is a different, reduced contract — see 26-permissions-access-ux.md's field-level
// access matrix.
public sealed record GetEmployeeTeamViewResponse(
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
    DateOnly StartDate,
    string Status,
    string? EmployeeNumber,
    Guid? EmploymentTypeId,
    string? EmploymentTypeName,
    bool ShowOnboardingTab,
    bool ShowProbationTab,
    bool ShowOffboardingTab,
    bool ShowLeavingTab,
    string? ProfilePhotoUrl);


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
    public bool AddressRequired { get; set; }
    [RequiredWhen(nameof(AddressRequired), ErrorMessage = "Address line 1 is required.")]
    public string AddressLine1 { get; set; } = string.Empty;
    public string AddressLine2 { get; set; } = string.Empty;
    [RequiredWhen(nameof(AddressRequired), ErrorMessage = "City is required.")]
    public string City { get; set; } = string.Empty;
    public string County { get; set; } = string.Empty;
    private string _postCode = string.Empty;
    [RequiredWhen(nameof(AddressRequired), ErrorMessage = "Postcode is required.")]
    [DynamicRegex(nameof(PostcodeRegexPattern), ErrorMessage = "Enter a valid postcode.")]
    public string PostCode
    {
        get => _postCode;
        set => _postCode = value.ToUpperInvariant();
    }
    public string Country { get; set; } = "United Kingdom";

    public string? PostcodeRegexPattern { get; set; }
    public string? TelephoneRegexPattern { get; set; }
    public string? MobileRegexPattern { get; set; }
    [RequiredUnless(nameof(EmployeeNumberAutoAssigned), ErrorMessage = "Employee number is required.")]
    public string EmployeeNumber { get; set; } = string.Empty;

    public bool EmployeeNumberAutoAssigned { get; set; }
    [Required(ErrorMessage = "Employment type is required.")]
    public Guid? EmploymentTypeId { get; set; }
    [Required(ErrorMessage = "Department is required.")]
    public Guid? DepartmentId { get; set; }
    [Required(ErrorMessage = "Location is required.")]
    public Guid? LocationId { get; set; }
    [Required(ErrorMessage = "Position profile is required.")]
    public Guid? PositionProfileId { get; set; }
    public bool ManagerRequired { get; set; }
    [RequiredWhen(nameof(ManagerRequired), ErrorMessage = "Select a manager or choose 'No manager — top-level role'.")]
    public Guid? ManagerId { get; set; }
    public bool HasSystemAccess { get; set; } = true;
    public bool OverrideWorkingPattern { get; set; } = false;
    public HashSet<string> WorkingWeek { get; set; } = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"];
    public decimal HoursPerDay { get; set; } = 7.5m;

    public bool CompensationRequired { get; set; }
    public string SalaryType { get; set; } = "Annual";
    [RequiredWhen(nameof(CompensationRequired), ErrorMessage = "Please enter a salary.")]
    [Range(0.01, double.MaxValue, ErrorMessage = "Salary must be greater than 0.")]
    public decimal? Salary { get; set; }
    [RequiredWhen(nameof(CompensationRequired), ErrorMessage = "Please enter a currency code.")]
    [StringLength(3, MinimumLength = 3, ErrorMessage = "Currency must be a 3-letter code (e.g. GBP).")]
    public string Currency { get; set; } = "GBP";
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
    decimal? HoursPerDayOverride,
    Guid? CorrelationId = null,
    // Ticket 2: the Employee.Version loaded before editing. When set, the save is rejected with a
    // concurrency conflict if the record changed in the meantime.
    int? ExpectedVersion = null);

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
    DateTimeOffset UpdatedAt,
    int Version = 0);


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
    Guid? ManagerId = null,
    decimal? Salary = null,
    string? SalaryFrequency = null,
    string? Currency = null);

public record CreateEmployeeResponse(
    Guid Id,
    Guid CompanyId,
    string FirstName,
    string LastName,
    string WorkEmail,
    string Status,
    DateTimeOffset CreatedAt);


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
    string? Country,
    int Version = 0);

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
    string Country,
    int? ExpectedVersion = null);


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
    int? NoticePeriodLengthOverride = null,
    Guid? CorrelationId = null,
    // Ticket 2: optimistic-concurrency token loaded before editing.
    int? ExpectedVersion = null);

public record UpdateEmployeeProfileAndEmploymentRequest(
    Guid CompanyId,
    Guid Id,
    string FirstName,
    string LastName,
    string? PreferredName,
    string WorkEmail,
    string? PersonalEmail,
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
    NoticePeriodUnit? NoticePeriodUnitOverride,
    int? NoticePeriodLengthOverride,
    WorkingDays? WorkingDaysOverride,
    decimal? HoursPerDayOverride,
    string? Notes,
    Guid? CorrelationId = null,
    int? ExpectedVersion = null);

public record UpdateEmployeeProfileAndEmploymentResponse(int Version = 0);


public sealed record StartLeavingProcessRequest(
    Guid CompanyId,
    Guid EmployeeId,
    DateOnly ResignationReceivedDate,
    DateOnly LeavingDate,
    DateOnly LastWorkingDay,
    string LeavingReason,
    bool ConfirmBackdatedLeavingDate = false,
    string? Notes = null);

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
    DateTimeOffset StartedAt,
    string? Notes = null);

public sealed record LeavingProcessResponse(
    Guid Id,
    DateOnly ResignationReceivedDate,
    DateOnly LeavingDate,
    DateOnly LastWorkingDay,
    NoticePeriodUnit NoticePeriodUnit,
    int NoticePeriodLength,
    string NoticeSource,
    string LeavingReason,
    string Status,
    // Ticket 2: optimistic-concurrency token.
    int Version = 0,
    DateTimeOffset StartedAt = default,
    string? CancellationReason = null,
    string? Notes = null);

public sealed record LeavingProcessLookupResult(LeavingProcessResponse? Process, bool NotFound, bool Failed)
{
    public static LeavingProcessLookupResult SuccessResult(LeavingProcessResponse? process) => new(process, false, false);
    public static LeavingProcessLookupResult NotFoundResult() => new(null, true, false);
    public static LeavingProcessLookupResult FailedResult() => new(null, false, true);
}

public sealed record AmendLeavingProcessRequest(
    Guid CompanyId,
    Guid EmployeeId,
    DateOnly LeavingDate,
    DateOnly LastWorkingDay,
    string LeavingReason,
    bool ConfirmBackdatedLeavingDate = false,
    // Ticket 2: optimistic-concurrency token loaded before editing.
    int? ExpectedVersion = null,
    string? Notes = null);

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
    bool OffboardingAlreadyStarted,
    // Ticket 2: optimistic-concurrency token after the update.
    int Version = 0,
    string? Notes = null);

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


public sealed record GetLeavingProcessHistoryResponse(IReadOnlyList<LeavingProcessHistoryItem> Items);

public sealed record LeavingProcessHistoryItem(
    Guid Id,
    string Status,
    DateOnly ResignationReceivedDate,
    DateOnly LeavingDate,
    DateOnly LastWorkingDay,
    string LeavingReason,
    string? Notes,
    string? ReplacementManagerName,
    DateTimeOffset StartedAt,
    DateTimeOffset? CancelledAt,
    string? CancellationReason,
    DateTimeOffset? FinalisationCompletedAt,
    DateTimeOffset UpdatedAt,
    bool IsCurrent);



public record CompleteInitialEmployeeSetupRequest(
    string FirstName,
    string LastName,
    string? PreferredName,
    DateOnly DateOfBirth,
    string Nationality,
    string Gender,
    string? GenderOther,
    string? PersonalEmail,
    string? PhoneNumber,
    string? HomePhone,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string? County,
    string PostCode,
    string? Country);

public record CompleteInitialEmployeeSetupResponse(
    Guid EmployeeId,
    bool RequiresInitialSetup,
    string Status);

public record ListNationalitiesResponse(IReadOnlyList<NationalityListItem> Items);

public record NationalityListItem(int Id, string Name);


public enum GenderIdentityChoice { NotSpecified, Man, Woman, NonBinary, SelfDescribed, PreferNotToSay }

public enum YesNoChoice { NotSpecified, Yes, No, PreferNotToSay }

public enum EthnicGroupChoice
{
    NotSpecified, White, Mixed, AsianOrAsianBritish,
    BlackOrAfricanOrCaribbeanOrBlackBritish, OtherEthnicGroup, SelfDescribed, PreferNotToSay
}

public enum SexualOrientationChoice
{
    NotSpecified, HeterosexualOrStraight, GayOrLesbian, Bisexual, Other, SelfDescribed, PreferNotToSay
}

public enum ReligionOrBeliefChoice
{
    NotSpecified, NoReligion, Christian, Buddhist, Hindu, Jewish, Muslim, Sikh,
    OtherReligion, SelfDescribed, PreferNotToSay
}

public sealed record GetMyEqualityDataResponse(
    bool HasRecord,
    GenderIdentityChoice? GenderIdentity,
    string? GenderIdentitySelfDescribed,
    YesNoChoice? MarriedOrCivilPartnershipStatus,
    EthnicGroupChoice? EthnicGroup,
    string? EthnicGroupSelfDescribed,
    YesNoChoice? DisabilityStatus,
    string? DisabilityImpact,
    SexualOrientationChoice? SexualOrientation,
    string? SexualOrientationSelfDescribed,
    ReligionOrBeliefChoice? ReligionOrBelief,
    string? ReligionOrBeliefSelfDescribed,
    YesNoChoice? CaringResponsibilities,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record SaveMyEqualityDataRequest(
    Guid CompanyId,
    Guid EmployeeId,
    GenderIdentityChoice? GenderIdentity,
    string? GenderIdentitySelfDescribed,
    YesNoChoice? MarriedOrCivilPartnershipStatus,
    EthnicGroupChoice? EthnicGroup,
    string? EthnicGroupSelfDescribed,
    YesNoChoice? DisabilityStatus,
    string? DisabilityImpact,
    SexualOrientationChoice? SexualOrientation,
    string? SexualOrientationSelfDescribed,
    ReligionOrBeliefChoice? ReligionOrBelief,
    string? ReligionOrBeliefSelfDescribed,
    YesNoChoice? CaringResponsibilities);

public sealed record EqualityFieldOption<T>(T Value, string Label);


public sealed record GetEqualityDiversityReportResponse(
    int TotalEmployees,
    int RespondentCount,
    decimal RespondentPercentage,
    DateOnly ReportingDate,
    int MinimumGroupSize,
    IReadOnlyList<EqualityReportDimensionModel> Dimensions);

public sealed record EqualityReportDimensionModel(
    string Key,
    string Name,
    IReadOnlyList<EqualityReportRowModel> Rows);

public sealed record EqualityReportRowModel(
    string Value,
    int Count,
    decimal Percentage,
    bool Suppressed);


public sealed record GetHeadcountSummaryResponse(IReadOnlyList<HeadcountSummaryItem> Items);

public sealed record HeadcountSummaryItem(
    Guid? DepartmentId,
    string DepartmentName,
    int EmployeeCount);


public sealed record GetNewHiresTrendResponse(IReadOnlyList<NewHiresTrendItem> Items);

public sealed record NewHiresTrendItem(
    int Year,
    int Month,
    string MonthLabel,
    int NewHireCount);


public sealed record GetRecentEmployeeChangesResponse(IReadOnlyList<RecentEmployeeChangeItem> Items);

public sealed record RecentEmployeeChangeItem(
    DateTimeOffset OccurredAt,
    string EmployeeName,
    string Action,
    string ActorName);


public sealed record GetMyTeamResponse(IReadOnlyList<TeamMemberItem> Items);

public sealed record TeamMemberItem(
    Guid EmployeeId,
    string FullName,
    string? JobTitle,
    string? PhoneNumber,
    string WorkEmail,
    string? ProfilePhotoUrl,
    string Status);


public sealed record GetMyTeamRosterResponse(IReadOnlyList<TeamRosterItem> Items);

public sealed record TeamRosterItem(
    Guid EmployeeId,
    string FirstName,
    string LastName,
    string? PreferredName,
    string? JobTitle,
    string WorkEmail,
    string? ProfilePhotoUrl,
    string Status)
{
    public string FullName => $"{FirstName} {LastName}";
}


public sealed record TeamStatusSummaryResponse(
    int TeamSize,
    int AtWork,
    int AwayToday,
    int OnLeave,
    int Sick,
    int InProbation,
    int MissingFitNotes,
    int NotScheduledToday,
    IReadOnlyList<TeamStatusMemberItem> Members);

public sealed record TeamStatusMemberItem(
    Guid EmployeeId,
    string FullName,
    string? JobTitle,
    bool OnLeaveToday,
    bool OffSickToday,
    bool InProbation,
    bool MissingFitNote,
    bool ScheduledToday,
    string PrimaryStatus);


public sealed record GetGenderSplitResponse(IReadOnlyList<GenderSplitItem> Items);

public sealed record GenderSplitItem(
    string Gender,
    int EmployeeCount,
    double Percentage);


public sealed record GetEmploymentTypeSplitResponse(IReadOnlyList<EmploymentTypeSplitItem> Items);

public sealed record EmploymentTypeSplitItem(
    Guid? EmploymentTypeId,
    string EmploymentTypeName,
    int EmployeeCount,
    double Percentage);


public enum WorkEmailSuggestionStatus
{
    Disabled,
    NotConfigured,
    NameIncomplete,
    Available,
    Unavailable,
}

public record WorkEmailSuggestionResponse(
    WorkEmailSuggestionStatus Status,
    string? Suggestion);
