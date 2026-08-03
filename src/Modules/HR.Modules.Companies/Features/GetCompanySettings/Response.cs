namespace HR.Modules.Companies.Features.GetCompanySettings;

internal sealed record GetCompanySettingsResponse(
    Guid CompanyId,
    string TimeZone,
    string Locale,
    string PostcodeRegex,
    string TelephoneRegex,
    string MobileRegex,
    DateTimeOffset UpdatedAt);
