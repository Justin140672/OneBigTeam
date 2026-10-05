using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Web.Models;


public record GetCompanyResponse(
    Guid Id,
    string Name,
    bool IsActive,
    DateTime CreatedAt,
    List<GetCompanyAddressResponse> Addresses,
    GetCompanyBrandingResponse? Branding,
    // Ticket 2: optimistic-concurrency token for the company aggregate.
    int Version = 0);

public record GetCompanyBrandingResponse(
    string? PrimaryLogoUrl,
    string? SmallLogoUrl,
    string? EmailLogoUrl);

public record GetCompanyAddressResponse(
    Guid Id,
    string Type,
    string? Line1,
    string? Line2,
    string? City,
    string? Region,
    string? PostalCode,
    string? CountryCode);


public record UpdateCompanyRequest(
    Guid Id,
    string Name,
    List<UpdateCompanyAddressRequest> Addresses,
    // Ticket 2: optimistic-concurrency token loaded before editing.
    int? ExpectedVersion = null);

public record UpdateCompanyAddressRequest(
    string Type,
    string? Line1,
    string? Line2,
    string? City,
    string? Region,
    string? PostalCode,
    string? CountryCode);

public record UpdateCompanyResponse(Guid Id, string Name, bool IsActive, int Version = 0);


public record GetCompanySettingsResponse(
    Guid CompanyId,
    string TimeZone,
    string Locale,
    string PostcodeRegex,
    string TelephoneRegex,
    string MobileRegex,
    DateTime UpdatedAt);

public record UpdateCompanySettingsRequest(
    Guid Id,
    string? TimeZone,
    string? Locale);

public record UpdateCompanySettingsResponse(
    Guid CompanyId,
    string? TimeZone,
    string? Locale,
    DateTime UpdatedAt);


public record GetHrSettingsResponse(
    Guid CompanyId,
    int WorkingDays,
    decimal HoursPerDay,
    int LeaveYearStartMonth,
    decimal DefaultHolidayAllowance,
    int ProbationMonths,
    bool ExcludePublicHolidaysFromLeave,
    bool ExcludePublicHolidaysFromSickness,
    bool DisplaySalaryOnEmployeeProfile,
    int FitNoteRequiredAfterDays,
    int ReturnToWorkRequiredAfterDays,
    string DefaultAcknowledgementStatement,
    int AcknowledgementReminderIntervalDays,
    NoticePeriodUnit NoticePeriodUnit,
    int NoticePeriodLength,
    bool AutoDisableAccessOnLeavingDate,
    EmployeeNumberMode EmployeeNumberMode,
    string? EmployeeNumberPrefix,
    int NextEmployeeNumber,
    int EmployeeNumberMinimumLength,
    AssetNumberMode AssetNumberMode,
    string? AssetNumberPrefix,
    int NextAssetNumber,
    int AssetNumberMinimumLength,
    DateTime UpdatedAt,
    int Version);

public record UpdateHrSettingsRequest(
    Guid Id,
    WorkingDays WorkingDays,
    decimal HoursPerDay,
    int LeaveYearStartMonth,
    decimal DefaultHolidayAllowance,
    int ProbationMonths,
    bool ExcludePublicHolidaysFromLeave,
    bool ExcludePublicHolidaysFromSickness,
    bool DisplaySalaryOnEmployeeProfile,
    int FitNoteRequiredAfterDays,
    int ReturnToWorkRequiredAfterDays,
    string? DefaultAcknowledgementStatement,
    int AcknowledgementReminderIntervalDays,
    NoticePeriodUnit NoticePeriodUnit,
    int NoticePeriodLength,
    bool AutoDisableAccessOnLeavingDate,
    EmployeeNumberMode EmployeeNumberMode,
    string? EmployeeNumberPrefix,
    int? NextEmployeeNumber,
    int EmployeeNumberMinimumLength,
    AssetNumberMode AssetNumberMode,
    string? AssetNumberPrefix,
    int NextAssetNumber,
    int AssetNumberMinimumLength,
    int Version);

public record UpdateHrSettingsResponse(
    Guid CompanyId,
    WorkingDays WorkingDays,
    decimal HoursPerDay,
    int LeaveYearStartMonth,
    decimal DefaultHolidayAllowance,
    int ProbationMonths,
    bool ExcludePublicHolidaysFromLeave,
    bool ExcludePublicHolidaysFromSickness,
    bool DisplaySalaryOnEmployeeProfile,
    int FitNoteRequiredAfterDays,
    int ReturnToWorkRequiredAfterDays,
    string DefaultAcknowledgementStatement,
    int AcknowledgementReminderIntervalDays,
    NoticePeriodUnit NoticePeriodUnit,
    int NoticePeriodLength,
    bool AutoDisableAccessOnLeavingDate,
    EmployeeNumberMode EmployeeNumberMode,
    string? EmployeeNumberPrefix,
    int NextEmployeeNumber,
    int EmployeeNumberMinimumLength,
    DateTime UpdatedAt,
    int Version);


public record UploadCompanyLogoResponse(
    Guid CompanyId,
    string AssetType,
    string? LogoUrl,
    DateTime UpdatedAt);

public record WorkEmailConventionExampleModel(WorkEmailNamingConvention Convention, string LocalPart);

public record GetWorkEmailSettingsResponse(
    Guid CompanyId,
    bool SuggestionsEnabled,
    string? PrimaryDomain,
    WorkEmailNamingConvention NamingConvention,
    string ExampleFirstName,
    string ExampleLastName,
    List<WorkEmailConventionExampleModel> Examples,
    DateTimeOffset UpdatedAt,
    int Version);

public record UpdateWorkEmailSettingsRequest(
    bool SuggestionsEnabled,
    string? PrimaryDomain,
    WorkEmailNamingConvention NamingConvention,
    int Version);

public record UpdateWorkEmailSettingsResponse(
    Guid CompanyId,
    bool SuggestionsEnabled,
    string? PrimaryDomain,
    WorkEmailNamingConvention NamingConvention,
    DateTimeOffset UpdatedAt,
    int Version);
