namespace HR.Modules.Companies.Contracts;

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
