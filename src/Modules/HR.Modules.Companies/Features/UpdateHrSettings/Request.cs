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
	public int NextEmployeeNumber { get; init; } = 1;
	public int EmployeeNumberMinimumLength { get; init; } = 1;
	public AssetNumberMode AssetNumberMode { get; init; } = AssetNumberMode.Manual;
	public string? AssetNumberPrefix { get; init; }
	public int NextAssetNumber { get; init; } = 1;
	public int AssetNumberMinimumLength { get; init; } = 1;
}
