namespace HR.Modules.Companies.Features.GetCompanySettings;

internal sealed record GetCompanySettingsResponse(
    Guid CompanyId,
    string TimeZone,
    string Locale,
    int WorkingDays,
    decimal HoursPerDay,
    int LeaveYearStartMonth,
    decimal DefaultHolidayAllowance,
    int ProbationMonths,
    bool ExcludePublicHolidaysFromLeave,
    bool ExcludePublicHolidaysFromSickness,
    int? FitNoteRequiredAfterDays,
    int? ReturnToWorkRequiredAfterDays,
    DateTimeOffset UpdatedAt);
