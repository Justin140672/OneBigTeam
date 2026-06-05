using HR.Modules.Companies.Domain;

namespace HR.Modules.Companies.Contracts.Events;

internal sealed record CompanySettingsUpdatedIntegrationEvent(
	Guid CompanyId,
	Guid? PerformedByUserId,
	DateTimeOffset OccurredAt,
	CompanySettingsEventSnapshot Previous,
	CompanySettingsEventSnapshot Current);

internal sealed record CompanySettingsEventSnapshot(
	string TimeZone,
	string Locale,
	WorkingDays WorkingWeek,
	int LeaveYearStartMonth,
	decimal DefaultHolidayAllowance,
	int ProbationMonths);
