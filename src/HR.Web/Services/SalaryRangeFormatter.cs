using System.Globalization;

namespace HR.Web.Services;

internal static class SalaryRangeFormatter
{
    public static string? Format(decimal? min, decimal? max, string? salaryType, CultureInfo culture)
    {
        if (min is null && max is null)
            return null;

        var period = salaryType switch
        {
            "Hourly" => "hour",
            "Daily" => "day",
            _ => "year"
        };

        var text = (min, max) switch
        {
            ({ } lo, { } hi) => $"{lo.ToString("C0", culture)}–{hi.ToString("C0", culture)}",
            ({ } lo, null) => $"From {lo.ToString("C0", culture)}",
            (null, { } hi) => $"Up to {hi.ToString("C0", culture)}",
            _ => null
        };

        return text is null ? null : $"{text} per {period}";
    }
}
