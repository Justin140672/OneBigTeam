namespace HR.Web.Components.Pages.Dashboards;

public static class DashboardAnnouncements
{
    public static string LoadComplete(string dashboardName) => $"{dashboardName} finished loading.";

    public static string? Counts(IEnumerable<(string Label, int Count)> counts)
    {
        var parts = counts.Select(c => $"{c.Count} {c.Label}").ToList();
        return parts.Count == 0 ? null : string.Join(", ", parts) + ".";
    }

    public static string? PartialFailure(IEnumerable<string> failedSources)
    {
        var parts = failedSources.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        return parts.Count == 0 ? null : $"Some information could not be loaded: {string.Join(", ", parts)}.";
    }

    public static string Compose(params string?[] parts) =>
        string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
}
