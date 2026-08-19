namespace HR.Modules.Companies.Features.UpdateCompanySettings;

internal sealed record UpdateCompanySettingsRequest
{
	public Guid CompanyId { get; init; }
	public string TimeZone { get; init; } = string.Empty;
	public string Locale { get; init; } = string.Empty;
}
