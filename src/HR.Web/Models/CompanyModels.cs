using HR.Infrastructure.Abstractions;
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

// ── SETTINGS ──────────────────────────────────────────────────────────────────

public record GetCompanySettingsResponse(
    Guid CompanyId,
    string TimeZone,
    string Locale,
    int WorkingDays,
    decimal HoursPerDay,
    int LeaveYearStartMonth,
    decimal DefaultHolidayAllowance,
    int ProbationMonths,
    bool ExcludePublicHolidaysFromLeave,
    bool ExcludePublicHolidaysFromSickness,
    bool DisplaySalaryOnEmployeeProfile,
    int? FitNoteRequiredAfterDays,
    int? ReturnToWorkRequiredAfterDays,
    string PostcodeRegex,
    string TelephoneRegex,
    string MobileRegex,
    string DefaultAcknowledgementStatement,
    int AcknowledgementReminderIntervalDays,
    DateTime UpdatedAt);

public record UpdateCompanySettingsRequest(
    Guid Id,
    string? TimeZone,
    string? Locale,
    WorkingDays WorkingDays,
    decimal HoursPerDay,
    int LeaveYearStartMonth,
    decimal DefaultHolidayAllowance,
    int ProbationMonths,
    bool ExcludePublicHolidaysFromLeave,
    bool ExcludePublicHolidaysFromSickness,
    bool DisplaySalaryOnEmployeeProfile,
    int? FitNoteRequiredAfterDays,
    int? ReturnToWorkRequiredAfterDays,
    string? DefaultAcknowledgementStatement,
    int AcknowledgementReminderIntervalDays);

public record UpdateCompanySettingsResponse(
    Guid CompanyId,
    string? TimeZone,
    string? Locale,
    WorkingDays WorkingDays,
    decimal HoursPerDay,
    int LeaveYearStartMonth,
    decimal DefaultHolidayAllowance,
    int ProbationMonths,
    bool ExcludePublicHolidaysFromLeave,
    bool ExcludePublicHolidaysFromSickness,
    bool DisplaySalaryOnEmployeeProfile,
    int? FitNoteRequiredAfterDays,
    int? ReturnToWorkRequiredAfterDays,
    string DefaultAcknowledgementStatement,
    int AcknowledgementReminderIntervalDays,
    DateTime UpdatedAt);

// ── LOGO UPLOAD ───────────────────────────────────────────────────────────────

public record UploadCompanyLogoResponse(
    Guid CompanyId,
    string AssetType,
    string? LogoUrl,
    DateTime UpdatedAt);
