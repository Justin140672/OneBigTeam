using HR.SharedKernel;

namespace HR.Modules.Leave.Domain;

internal static class LeaveCalculator
{
    internal static decimal CalculateTotalDays(
        DateOnly startDate, LeaveDayPart startPart,
        DateOnly endDate, LeaveDayPart endPart,
        WorkingPattern pattern,
        IReadOnlyCollection<DateOnly>? publicHolidays = null)
    {
        decimal totalHours = 0;
        var current = startDate;

        while (current <= endDate)
        {
            if (pattern.IsWorkingDay(current.DayOfWeek) &&
                (publicHolidays is null || !publicHolidays.Contains(current)))
            {
                totalHours += current == startDate && current == endDate
                    ? PartToHours(startPart, pattern.HoursPerDay)
                    : current == startDate
                        ? PartToHours(startPart, pattern.HoursPerDay)
                        : current == endDate
                            ? PartToHours(endPart, pattern.HoursPerDay)
                            : pattern.HoursPerDay;
            }

            current = current.AddDays(1);
        }

        return totalHours / pattern.HoursPerDay;
    }

    private static decimal PartToHours(LeaveDayPart part, decimal hoursPerDay) =>
        part == LeaveDayPart.FullDay ? hoursPerDay : hoursPerDay / 2;
}
