namespace HR.Modules.Companies.Contracts;

/// <summary>
/// Fallback reader for a company's sickness configuration. <see cref="Default"/> must stay in
/// sync with CompanySettings.CreateDefault (HR.Modules.Companies.Domain) — both represent the
/// same confirmed defaults (SICK-05: ReturnToWorkRequiredAfterDays = 1 working day,
/// FitNoteRequiredAfterDays = 7 calendar days). See
/// specifications/product-specifications/00-current-product-decisions.md ("Sickness management").
/// </summary>
public sealed record CompanySicknessSettings(
    bool ExcludePublicHolidaysFromSickness,
    int FitNoteRequiredAfterDays,
    int ReturnToWorkRequiredAfterDays,
    int FrequentAbsenceCountThreshold,
    int FrequentAbsenceWindowDays,
    int LongAbsenceDayThreshold,
    int WeekdayPatternOccurrenceThreshold,
    int WeekdayPatternWindowDays)
{
    public static CompanySicknessSettings Default { get; } = new(
        ExcludePublicHolidaysFromSickness: false,
        FitNoteRequiredAfterDays: 7,
        ReturnToWorkRequiredAfterDays: 1,
        FrequentAbsenceCountThreshold: 4,
        FrequentAbsenceWindowDays: 365,
        LongAbsenceDayThreshold: 28,
        WeekdayPatternOccurrenceThreshold: 3,
        WeekdayPatternWindowDays: 365);
}
