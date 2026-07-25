using HR.Infrastructure.Abstractions;
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
	bool ExcludePublicHolidaysFromSickness,
	bool DisplaySalaryOnEmployeeProfile,
	int? FitNoteRequiredAfterDays,
	int? ReturnToWorkRequiredAfterDays,
	string DefaultAcknowledgementStatement,
	int AcknowledgementReminderIntervalDays,
	NoticePeriodUnit NoticePeriodUnit,
	int NoticePeriodLength,
	bool AutoDisableAccessOnLeavingDate,
	DateTimeOffset UpdatedAt);
