namespace HR.Web.Components.Pages.Dashboards;

/// <summary>
/// Pure key-to-next-index logic for the WAI-ARIA tabs keyboard pattern used by the
/// operational dashboards (DSH-07). Extracted from the Blazor component so it can be
/// unit tested with no Blazor / rendering dependencies.
/// </summary>
public static class DashboardTabKeyboard
{
    /// <summary>
    /// Given a keyboard key, the currently-selected tab index and the total number of
    /// tabs, returns the index that should become selected/focused, or <c>null</c> if the
    /// key is not one the tablist handles.
    /// ArrowLeft/ArrowRight wrap around; Home selects the first tab; End selects the last.
    /// </summary>
    public static int? NextIndex(string key, int currentIndex, int tabCount)
    {
        if (tabCount <= 0)
            return null;

        return key switch
        {
            "ArrowLeft" or "ArrowUp" => (currentIndex - 1 + tabCount) % tabCount,
            "ArrowRight" or "ArrowDown" => (currentIndex + 1) % tabCount,
            "Home" => 0,
            "End" => tabCount - 1,
            _ => null,
        };
    }
}
