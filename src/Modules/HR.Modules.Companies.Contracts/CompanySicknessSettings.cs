namespace HR.Modules.Companies.Contracts;

public sealed record CompanySicknessSettings(
    bool ExcludePublicHolidaysFromSickness,
    int FitNoteRequiredAfterDays,
    int ReturnToWorkRequiredAfterDays)
{
    public static CompanySicknessSettings Default { get; } = new(
        ExcludePublicHolidaysFromSickness: false,
        FitNoteRequiredAfterDays: 7,
        ReturnToWorkRequiredAfterDays: 1);
}
