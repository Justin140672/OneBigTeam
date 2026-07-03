namespace HR.Infrastructure.Abstractions;

public sealed record WorkingPattern(WorkingDays WorkingDays, decimal HoursPerDay)
{
    public static readonly WorkingPattern Default = new(
        WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday |
        WorkingDays.Thursday | WorkingDays.Friday,
        7.5m);

    public bool IsWorkingDay(DayOfWeek dayOfWeek) => dayOfWeek switch
    {
        DayOfWeek.Monday    => WorkingDays.HasFlag(WorkingDays.Monday),
        DayOfWeek.Tuesday   => WorkingDays.HasFlag(WorkingDays.Tuesday),
        DayOfWeek.Wednesday => WorkingDays.HasFlag(WorkingDays.Wednesday),
        DayOfWeek.Thursday  => WorkingDays.HasFlag(WorkingDays.Thursday),
        DayOfWeek.Friday    => WorkingDays.HasFlag(WorkingDays.Friday),
        DayOfWeek.Saturday  => WorkingDays.HasFlag(WorkingDays.Saturday),
        DayOfWeek.Sunday    => WorkingDays.HasFlag(WorkingDays.Sunday),
        _                   => false
    };
}
