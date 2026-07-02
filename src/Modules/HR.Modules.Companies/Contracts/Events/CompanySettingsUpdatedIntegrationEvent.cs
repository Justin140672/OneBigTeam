using HR.SharedKernel;

namespace HR.Modules.Companies.Contracts.Events;

internal sealed record CompanySettingsUpdatedIntegrationEvent(
	Guid CompanyId,
	string TimeZone,
	string Locale,
	WorkingDays WorkingDays,
	decimal HoursPerDay,
	int LeaveYearStartMonth,
	decimal DefaultHolidayAllowance,
	int ProbationMonths,
	bool ExcludePublicHolidaysFromLeave,
	bool ExcludePublicHolidaysFromSickness,
	DateTimeOffset OccurredAt);
