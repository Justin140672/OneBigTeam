namespace HR.Web.Models;

public static class TaskTitleFormatter
{
    public static string StripEmployeeSuffix(string title, params string?[] employeeNames)
    {
        foreach (var name in employeeNames)
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var suffix = $" — {name.Trim()}";
            if (title.Length > suffix.Length && title.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return title[..^suffix.Length].TrimEnd();
        }

        return title;
    }
}
