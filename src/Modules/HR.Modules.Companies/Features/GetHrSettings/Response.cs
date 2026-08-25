using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;

namespace HR.Modules.Companies.Features.GetHrSettings;


internal sealed record GetHrSettingsResponse(
    Guid CompanyId,
    int WorkingDays,
    decimal HoursPerDay,
    int LeaveYearStartMonth,
    decimal DefaultHolidayAllowance,
    int ProbationMonths,
    bool ExcludePublicHolidaysFromLeave,
    bool ExcludePublicHolidaysFromSickness,
    bool DisplaySalaryOnEmployeeProfile,
    int FitNoteRequiredAfterDays,
    int ReturnToWorkRequiredAfterDays,
    string DefaultAcknowledgementStatement,
    int AcknowledgementReminderIntervalDays,
    NoticePeriodUnit NoticePeriodUnit,
    int NoticePeriodLength,
    bool AutoDisableAccessOnLeavingDate,
    EmployeeNumberMode EmployeeNumberMode,
    string? EmployeeNumberPrefix,
    int NextEmployeeNumber,
    int EmployeeNumberMinimumLength,
    AssetNumberMode AssetNumberMode,
    string? AssetNumberPrefix,
    int NextAssetNumber,
    int AssetNumberMinimumLength,
    DateTimeOffset UpdatedAt,
    int Version);
