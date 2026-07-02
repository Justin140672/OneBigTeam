namespace HR.SharedKernel;

public sealed record CompanySicknessSettings(bool ExcludePublicHolidaysFromSickness)
{
    public static CompanySicknessSettings Default { get; } = new(ExcludePublicHolidaysFromSickness: false);
}
