using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Web.Models;

// ── GET ──────────────────────────────────────────────────────────────────────

public record GetCompanyResponse(
    Guid Id,
    string Name,
    bool IsActive,
    DateTime CreatedAt,
    List<GetCompanyAddressResponse> Addresses,
    GetCompanyBrandingResponse? Branding);

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

// ── UPDATE COMPANY ────────────────────────────────────────────────────────────

public record UpdateCompanyRequest(
    Guid Id,
    string Name,
    List<UpdateCompanyAddressRequest> Addresses);

public record UpdateCompanyAddressRequest(
    string Type,
    string? Line1,
    string? Line2,
    string? City,
    string? Region,
    string? PostalCode,
    string? CountryCode);

// Generic response envelope used for PUT /api/companies/{id}
public record UpdateCompanyResponse(Guid Id, string Name, bool IsActive);

// ── SETTINGS (Company Administrator territory — profile/regional only) ────────

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

// ── HR SETTINGS (HR Administrator territory — HR policy fields) ──────────────

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
    DateTime UpdatedAt);

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
    int NextEmployeeNumber,
    int EmployeeNumberMinimumLength,
    AssetNumberMode AssetNumberMode,
    string? AssetNumberPrefix,
    int NextAssetNumber,
    int AssetNumberMinimumLength);

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
    DateTime UpdatedAt);

// ── LOGO UPLOAD ───────────────────────────────────────────────────────────────

public record UploadCompanyLogoResponse(
    Guid CompanyId,
    string AssetType,
    string? LogoUrl,
    DateTime UpdatedAt);
