namespace HR.Web.Services;

public enum ShellLayoutMode
{
    Pending,
    Wide,
    Compact,
    Overlay,
}

public static class ShellBreakpoints
{
    public const int WideMinWidth = 1200;
    public const int CompactMinWidth = 768;

    public const string WideQuery = "(min-width: 1200px)";
    public const string CompactQuery = "(min-width: 768px)";

    public static ShellLayoutMode ForWidth(double viewportWidth) =>
        viewportWidth >= WideMinWidth ? ShellLayoutMode.Wide
        : viewportWidth >= CompactMinWidth ? ShellLayoutMode.Compact
        : ShellLayoutMode.Overlay;
}

public sealed class ShellNavState
{
    public ShellLayoutMode Mode { get; private set; } = ShellLayoutMode.Pending;

    public bool PreferenceExpanded { get; private set; } = true;

    public bool DrawerOpen { get; private set; }

    public bool IsWide => Mode is ShellLayoutMode.Wide or ShellLayoutMode.Pending;

    public bool UsesDrawer => !IsWide;

    public bool IsOpen => IsWide ? PreferenceExpanded : DrawerOpen;

    public string ModeName => Mode.ToString().ToLowerInvariant();

    public void RestorePreference(bool expanded) => PreferenceExpanded = expanded;

    public bool SetMode(ShellLayoutMode mode)
    {
        if (mode == Mode)
            return false;

        Mode = mode;
        DrawerOpen = false;
        return true;
    }

    public void SetOpen(bool open)
    {
        if (IsWide)
            PreferenceExpanded = open;
        else
            DrawerOpen = open;
    }

    public void Toggle() => SetOpen(!IsOpen);

    public bool CloseDrawer()
    {
        if (!UsesDrawer || !DrawerOpen)
            return false;

        DrawerOpen = false;
        return true;
    }
}
