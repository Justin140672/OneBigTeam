namespace HR.Infrastructure.Abstractions;

public sealed record CompanySicknessSettings(
    bool ExcludePublicHolidaysFromSickness,
    int? FitNoteRequiredAfterDays,
    int? ReturnToWorkRequiredAfterDays)
{
    public static CompanySicknessSettings Default { get; } = new(
        ExcludePublicHolidaysFromSickness: false,
        FitNoteRequiredAfterDays: null,
        ReturnToWorkRequiredAfterDays: null);
}
