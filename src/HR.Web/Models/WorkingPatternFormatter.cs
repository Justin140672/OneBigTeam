using HR.Infrastructure.Abstractions;

namespace HR.Web.Models;

public static class WorkingPatternFormatter
{
    private static readonly (WorkingDays Day, string Abbrev)[] Week =
    [
        (WorkingDays.Monday, "Mon"),
        (WorkingDays.Tuesday, "Tue"),
        (WorkingDays.Wednesday, "Wed"),
        (WorkingDays.Thursday, "Thu"),
        (WorkingDays.Friday, "Fri"),
        (WorkingDays.Saturday, "Sat"),
        (WorkingDays.Sunday, "Sun"),
    ];

    public static string Summarize(WorkingDays workingDays, decimal hoursPerDay) =>
        $"{FormatDays(workingDays)} ({hoursPerDay.ToString("0.##")}hrs/day)";

    private static string FormatDays(WorkingDays workingDays)
    {
        var groups = new List<List<string>>();
        var lastIndex = -2;

        for (var i = 0; i < Week.Length; i++)
        {
            if (!workingDays.HasFlag(Week[i].Day))
                continue;

            if (i == lastIndex + 1)
                groups[^1].Add(Week[i].Abbrev);
            else
                groups.Add([Week[i].Abbrev]);

            lastIndex = i;
        }

        return groups.Count == 0
            ? "None"
            : string.Join(", ", groups.Select(g => g.Count >= 3 ? $"{g[0]}-{g[^1]}" : string.Join(", ", g)));
    }
}
