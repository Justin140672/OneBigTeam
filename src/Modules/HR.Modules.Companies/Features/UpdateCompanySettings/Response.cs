namespace HR.Modules.Companies.Features.UpdateCompanySettings;

internal sealed record UpdateCompanySettingsResponse(
	Guid CompanyId,
	string TimeZone,
	string Locale,
	DateTimeOffset UpdatedAt,
	int Version);
