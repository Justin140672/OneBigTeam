using System.Globalization;

namespace HR.Marketing.Services;

public static class EmployeeCountInputParser
{
    public static EmployeeCountParseResult Parse(string? raw, int fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new EmployeeCountParseResult(0, null);
        }

        var trimmed = raw.Trim();

        if (trimmed.Contains('.') || trimmed.Contains(','))
        {
            return new EmployeeCountParseResult(fallback, "Please enter a whole number of employees, without decimals.");
        }

        if (!int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return new EmployeeCountParseResult(fallback, "Please enter a valid number of employees.");
        }

        if (value < 0)
        {
            return new EmployeeCountParseResult(fallback, "Employee count cannot be negative.");
        }

        return new EmployeeCountParseResult(value, null);
    }
}

public readonly record struct EmployeeCountParseResult(int Value, string? ValidationMessage);
