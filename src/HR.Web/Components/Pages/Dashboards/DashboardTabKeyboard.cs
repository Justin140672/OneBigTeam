namespace HR.Web.Components.Pages.Dashboards;

public static class DashboardTabKeyboard
{
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
