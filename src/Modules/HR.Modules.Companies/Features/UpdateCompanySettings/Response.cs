using HR.SharedKernel;

namespace HR.Modules.Companies.Features.UpdateCompanySettings;

internal sealed record UpdateCompanySettingsResponse(
	Guid CompanyId,
	string TimeZone,
	string Locale,
	WorkingDays WorkingDays,
	decimal HoursPerDay,
	int LeaveYearStartMonth,
	decimal DefaultHolidayAllowance,
	int ProbationMonths,
	bool ExcludePublicHolidaysFromLeave,
	DateTimeOffset UpdatedAt);
