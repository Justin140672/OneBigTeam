using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Sickness.Domain;

internal static class SicknessCalculator
{
    internal static decimal CalculateTotalDays(
        DateOnly startDate,
        SicknessDayPart startPart,
        DateOnly endDate,
        SicknessDayPart endPart,
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

        return pattern.HoursPerDay == 0 ? 0 : totalHours / pattern.HoursPerDay;
    }

    private static decimal PartToHours(SicknessDayPart part, decimal hoursPerDay) =>
        part == SicknessDayPart.FullDay ? hoursPerDay : hoursPerDay / 2;
}
