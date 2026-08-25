namespace HR.Modules.Companies.Features.UpdateCompanySettings;

internal sealed record UpdateCompanySettingsRequest
{
	public Guid CompanyId { get; init; }
	public string TimeZone { get; init; } = string.Empty;
	public string Locale { get; init; } = string.Empty;

	/// <summary>
	/// SET-03: the settings version the client last read (from GetCompanySettingsResponse.Version).
	/// Compared against the persisted row's version at save time; a mismatch means the record was
	/// changed elsewhere since the client loaded it, and the update is rejected as a conflict.
	/// </summary>
	public int Version { get; init; }
}
