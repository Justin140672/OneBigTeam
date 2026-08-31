namespace HR.Web.Components.Pages.Dashboards;

/// <summary>
/// Pure string builders for the dashboards' aria-live status announcements (DSH-07).
/// No Blazor dependencies so the phrasing can be unit tested directly.
/// </summary>
public static class DashboardAnnouncements
{
    /// <summary>"{dashboardName} finished loading."</summary>
    public static string LoadComplete(string dashboardName) => $"{dashboardName} finished loading.";

    /// <summary>
    /// Joins "{count} {label}" pairs with ", " and terminates with a full stop, e.g.
    /// "3 open vacancies, 2 interviews requiring action." Labels are used verbatim
    /// (they are already plural noun phrases).
    /// </summary>
    public static string? Counts(IEnumerable<(string Label, int Count)> counts)
    {
        var parts = counts.Select(c => $"{c.Count} {c.Label}").ToList();
        return parts.Count == 0 ? null : string.Join(", ", parts) + ".";
    }

    /// <summary>
    /// "Some information could not be loaded: X, Y." or <c>null</c> when nothing failed.
    /// </summary>
    public static string? PartialFailure(IEnumerable<string> failedSources)
    {
        var parts = failedSources.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        return parts.Count == 0 ? null : $"Some information could not be loaded: {string.Join(", ", parts)}.";
    }

    /// <summary>Joins the non-null, non-empty parts with a single space.</summary>
    public static string Compose(params string?[] parts) =>
        string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
}
