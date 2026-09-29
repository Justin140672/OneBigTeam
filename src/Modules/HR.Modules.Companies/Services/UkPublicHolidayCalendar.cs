namespace HR.Modules.Companies.Services;

internal static class UkPublicHolidayCalendar
{
    public const string CountryCode = "GB";

    public static IReadOnlyList<(DateOnly Date, string Name)> GetEnglandAndWales(int year)
    {
        var holidays = new List<(DateOnly Date, string Name)>();

        var newYear = new DateOnly(year, 1, 1);
        holidays.Add(newYear.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
            ? (NextWeekday(newYear, []), "New Year's Day (substitute)")
            : (newYear, "New Year's Day"));

        var easterSunday = EasterSunday(year);
        holidays.Add((easterSunday.AddDays(-2), "Good Friday"));
        holidays.Add((easterSunday.AddDays(1), "Easter Monday"));
        holidays.Add((MondayOnOrAfter(new DateOnly(year, 5, 1)), "Early May Bank Holiday"));
        holidays.Add((LastMonday(year, 5), "Spring Bank Holiday"));
        holidays.Add((LastMonday(year, 8), "Summer Bank Holiday"));

        var christmas = new DateOnly(year, 12, 25);
        var boxingDay = new DateOnly(year, 12, 26);
        var taken = new HashSet<DateOnly>();
        if (!IsWeekend(christmas)) taken.Add(christmas);
        if (!IsWeekend(boxingDay)) taken.Add(boxingDay);

        foreach (var (natural, name) in new[] { (christmas, "Christmas Day"), (boxingDay, "Boxing Day") })
        {
            if (!IsWeekend(natural))
            {
                holidays.Add((natural, name));
                continue;
            }

            var substitute = NextWeekday(natural, taken);
            taken.Add(substitute);
            holidays.Add((substitute, $"{name} (substitute)"));
        }

        return holidays.OrderBy(h => h.Date).ToList();
    }

    public static IReadOnlyList<(DateOnly Date, string Name)> GetUpcoming(DateOnly today, int yearsAhead)
    {
        return Enumerable.Range(today.Year, yearsAhead + 1)
            .SelectMany(GetEnglandAndWales)
            .Where(h => h.Date > today)
            .OrderBy(h => h.Date)
            .ToList();
    }

    public static DateOnly EasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var month = (h + l - 7 * m + 114) / 31;
        var day = (h + l - 7 * m + 114) % 31 + 1;
        return new DateOnly(year, month, day);
    }

    private static bool IsWeekend(DateOnly date) => date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    private static DateOnly NextWeekday(DateOnly from, HashSet<DateOnly> taken)
    {
        var candidate = from.AddDays(1);
        while (IsWeekend(candidate) || taken.Contains(candidate))
        {
            candidate = candidate.AddDays(1);
        }

        return candidate;
    }

    private static DateOnly MondayOnOrAfter(DateOnly date)
    {
        while (date.DayOfWeek != DayOfWeek.Monday)
        {
            date = date.AddDays(1);
        }

        return date;
    }

    private static DateOnly LastMonday(int year, int month)
    {
        var date = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        while (date.DayOfWeek != DayOfWeek.Monday)
        {
            date = date.AddDays(-1);
        }

        return date;
    }
}
