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
    bool DisplaySalaryOnEmployeeProfile,
    int? FitNoteRequiredAfterDays,
    int? ReturnToWorkRequiredAfterDays,
    string PostcodeRegex,
    string TelephoneRegex,
    string MobileRegex,
    DateTimeOffset UpdatedAt);
