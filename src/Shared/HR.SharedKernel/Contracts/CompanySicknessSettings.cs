namespace HR.SharedKernel;

public sealed record CompanySicknessSettings(bool ExcludePublicHolidaysFromSickness, int? FitNoteRequiredAfterDays)
{
    public static CompanySicknessSettings Default { get; } = new(ExcludePublicHolidaysFromSickness: false, FitNoteRequiredAfterDays: null);
}
