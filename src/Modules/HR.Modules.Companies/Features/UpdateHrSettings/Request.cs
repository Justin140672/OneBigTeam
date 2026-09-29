using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Companies.Features.UpdateHrSettings;

internal sealed record UpdateHrSettingsRequest
{
	public Guid CompanyId { get; init; }
	public WorkingDays WorkingDays { get; init; }
	public decimal HoursPerDay { get; init; }
	public int LeaveYearStartMonth { get; init; }
	public decimal DefaultHolidayAllowance { get; init; }
	public int ProbationMonths { get; init; }
	public bool ExcludePublicHolidaysFromLeave { get; init; } = true;
	public bool ExcludePublicHolidaysFromSickness { get; init; } = false;
	public bool DisplaySalaryOnEmployeeProfile { get; init; } = false;
	public int FitNoteRequiredAfterDays { get; init; } = 7;
	public int ReturnToWorkRequiredAfterDays { get; init; } = 1;
	public string DefaultAcknowledgementStatement { get; init; } = string.Empty;
	public int AcknowledgementReminderIntervalDays { get; init; } = 3;
	public NoticePeriodUnit NoticePeriodUnit { get; init; } = NoticePeriodUnit.Months;
	public int NoticePeriodLength { get; init; } = 1;
	public bool AutoDisableAccessOnLeavingDate { get; init; } = true;
	public EmployeeNumberMode EmployeeNumberMode { get; init; } = EmployeeNumberMode.Manual;
	public string? EmployeeNumberPrefix { get; init; }
	// Null = "not changed by the administrator": keep the company's LIVE counter. The counter is
	// advanced concurrently by Automatic-mode employee creation (EmployeeNumberGenerator's atomic
	// UPDATE, which deliberately doesn't bump Version), so echoing back the value the settings form
	// loaded would silently rewind it on every unrelated HR-settings save and hand out numbers that
	// are already taken. Only an explicit value (the admin edited "Next Number") is applied.
	public int? NextEmployeeNumber { get; init; }
	public int EmployeeNumberMinimumLength { get; init; } = 1;
	public AssetNumberMode AssetNumberMode { get; init; } = AssetNumberMode.Manual;
	public string? AssetNumberPrefix { get; init; }
	public int NextAssetNumber { get; init; } = 1;
	public int AssetNumberMinimumLength { get; init; } = 1;

	public int Version { get; init; }

	public int? ProbationCheckpointDay1 { get; init; }
	public int? ProbationCheckpointDay2 { get; init; }
	public int? ProbationCheckpointDay3 { get; init; }

	public int FrequentAbsenceCountThreshold { get; init; } = 4;
	public int FrequentAbsenceWindowDays { get; init; } = 365;
	public int LongAbsenceDayThreshold { get; init; } = 28;
	public int WeekdayPatternOccurrenceThreshold { get; init; } = 3;
	public int WeekdayPatternWindowDays { get; init; } = 365;
}
