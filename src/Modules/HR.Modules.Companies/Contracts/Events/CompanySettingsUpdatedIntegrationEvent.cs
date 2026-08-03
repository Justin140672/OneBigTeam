namespace HR.Modules.Companies.Contracts.Events;

internal sealed record CompanySettingsUpdatedIntegrationEvent(
	Guid CompanyId,
	string TimeZone,
	string Locale,
	DateTimeOffset OccurredAt);
