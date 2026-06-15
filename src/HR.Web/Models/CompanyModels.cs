using HR.SharedKernel;

namespace HR.Web.Models;

// ── GET ──────────────────────────────────────────────────────────────────────

public record GetCompanyResponse(
    Guid Id,
    string Name,
    string Slug,
    bool IsActive,
    DateTime CreatedAt,
    List<GetCompanyAddressResponse> Addresses,
    GetCompanyBrandingResponse? Branding);

public record GetCompanyBrandingResponse(
    string PrimaryColor,
    string SecondaryColor,
    string AccentColor,
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
    List<UpdateCompanyAddressRequest> Addresses,
    UpdateCompanyBrandingRequest? Branding = null);

public record UpdateCompanyAddressRequest(
    string Type,
    string? Line1,
    string? Line2,
    string? City,
    string? Region,
    string? PostalCode,
    string? CountryCode);

public record UpdateCompanyBrandingRequest(
    string? PrimaryColor,
    string? SecondaryColor,
    string? AccentColor);

// Generic response envelope used for PUT /api/companies/{id}
public record UpdateCompanyResponse(Guid Id, string Name, string Slug, bool IsActive);

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
    bool ExcludePublicHolidaysFromLeave);

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
    DateTime UpdatedAt);

// ── LOGO UPLOAD ───────────────────────────────────────────────────────────────

public record UploadCompanyLogoResponse(
    Guid CompanyId,
    string AssetType,
    string? LogoUrl,
    DateTime UpdatedAt);
