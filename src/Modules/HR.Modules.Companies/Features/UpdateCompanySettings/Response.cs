namespace HR.Modules.Companies.Features.UpdateCompanySettings;

internal sealed record UpdateCompanySettingsResponse(
	Guid CompanyId,
	string TimeZone,
	string Locale,
	string WorkingWeek,
	int LeaveYearStartMonth,
	decimal DefaultHolidayAllowance,
	int ProbationMonths,
	DateTimeOffset UpdatedAt);
