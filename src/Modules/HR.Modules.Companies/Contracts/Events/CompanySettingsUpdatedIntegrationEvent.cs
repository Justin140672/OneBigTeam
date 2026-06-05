namespace HR.Modules.Companies.Contracts.Events;

internal sealed record CompanySettingsUpdatedIntegrationEvent(
	Guid CompanyId,
	string TimeZone,
	string Locale,
	string WorkingWeek,
	int LeaveYearStartMonth,
	decimal DefaultHolidayAllowance,
	int ProbationMonths,
	DateTimeOffset OccurredAt);
