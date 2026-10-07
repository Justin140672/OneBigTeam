using HR.Modules.Employees.Contracts;

namespace HR.Modules.Employees.Services;

internal static class ProposedLastWorkingDayCalculator
{
    public const int LookbackDays = 60;

    public static DateOnly Calculate(
        DateOnly leavingDate,
        WorkingPattern pattern,
        IReadOnlyCollection<DateOnly> bankHolidays)
    {
        var effectivePattern = pattern.WorkingDays == WorkingDays.None ? WorkingPattern.Default : pattern;

        var candidate = leavingDate;

        for (var i = 0; i <= LookbackDays; i++)
        {
            if (effectivePattern.IsWorkingDay(candidate.DayOfWeek) && !bankHolidays.Contains(candidate))
                return candidate;

            candidate = candidate.AddDays(-1);
        }

        return leavingDate;
    }
}
