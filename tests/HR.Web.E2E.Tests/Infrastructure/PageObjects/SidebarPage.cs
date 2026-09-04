using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Interacts with the app sidebar rendered by MainLayout.razor (Syncfusion SfMenu,
/// CssClass="app-nav-menu"). Only top-level menu item labels are present in the DOM up front —
/// a nested submenu's children aren't rendered at all until their parent item is expanded, and
/// (per Syncfusion's usual popup-based submenu rendering) may render as a portal elsewhere in the
/// page rather than nested inside ".app-nav-menu" once they are (see OrganisationChartTests).
/// Every locator here is therefore scoped to exact text matches so that, e.g., asserting "Vacancies"
/// is visible can't accidentally match a similarly-named submenu label.
/// </summary>
public sealed class SidebarPage(IPage page)
{
    private ILocator NavMenu => page.Locator(".app-nav-menu");

    /// <summary>
    /// Returns true if the sidebar (MainLayout.razor's ShowSidebar) is rendered at all — false for
    /// a persona with no qualifying role. ShowSidebar depends on AppSession.IsManager/IsRecruiter/
    /// IsHrAdministrator, which only become true after AppSession's own async /api/me round-trip
    /// completes in OnInitializedAsync; on the very first render those flags default to false, so
    /// ".app-sidebar" may not be in the DOM yet even for a persona that will end up seeing it a
    /// moment later. A bounded wait (rather than an instant CountAsync snapshot) avoids racing that
    /// round-trip. The timeout only needs to be generous for the positive case — a genuine "no
    /// sidebar" persona still resolves as soon as MainLayout finishes rendering with ShowSidebar
    /// false, not by waiting out the full timeout — so a longer timeout here doesn't slow down
    /// negative-case tests, it just gives a genuinely-showing sidebar (circuit connect + /api/me +
    /// Syncfusion SfSidebar/SfMenu JS init, slower under SlowMo) enough room to actually render
    /// before being wrongly reported as absent.
    /// </summary>
    public async Task<bool> IsSidebarVisibleAsync()
    {
        try
        {
            await NavMenu.First.WaitForAsync(
                new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    /// <summary>
    /// Returns true if a menu item with exactly this text is present and visible without
    /// expanding any parent first — true for top-level items (including the flattened
    /// "Vacancies"/"Candidates" entries and the dashboard links), false for anything still
    /// nested under a collapsed parent (e.g. "Organisation Chart" under "People").
    /// </summary>
    public async Task<bool> HasTopLevelMenuItemAsync(string text)
    {
        // Same race as IsSidebarVisibleAsync above (AppSession's async /api/me round-trip has to
        // complete before ShowSidebar/the menu items exist at all) — an instant IsVisibleAsync()
        // snapshot can read false before that round-trip lands. Bound-wait for the specific item
        // instead of just the container, then fall through to false on genuine absence.
        try
        {
            await NavMenu.GetByText(text, new() { Exact = true }).First.WaitForAsync(
                new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    /// <summary>
    /// Clicks a top-level menu item by its exact text. Does not itself wait for the resulting
    /// navigation — callers should follow up with page.WaitForURLAsync for the expected route.
    /// </summary>
    public async Task ClickTopLevelMenuItemAsync(string text) =>
        await NavMenu.GetByText(text, new() { Exact = true }).ClickAsync();

    // ── Grouped items ─────────────────────────────────────────────────────────
    // MainLayout's admin nav (AdminNavigation.Sections) renders as SfMenu groups — a top-level
    // "HR configuration" / "People and users" / … item whose children ("HR Settings",
    // "Organisation Chart", …) only exist once the group is expanded, and Syncfusion renders that
    // submenu as a popup (role="menuitem" nodes portaled outside ".app-nav-menu").

    // Syncfusion's vertical SfMenu (ShowItemOnClick) is fussy under GetByRole's accessible-name
    // computation (leading icon spans, whitespace), so match on exact visible text instead — the
    // same approach OrganisationChartTests used before the nav was regrouped. The expanded
    // submenu's items may render as a popup portaled outside ".app-nav-menu", so search page-wide
    // for the child.
    private ILocator GroupHeader(string text) =>
        NavMenu.GetByText(text, new() { Exact = true });

    private ILocator ChildItem(string text) =>
        page.GetByText(text, new() { Exact = true });

    /// <summary>
    /// Ensures the named top-level nav group is expanded and its child <paramref name="itemText"/>
    /// is visible. Idempotent: a Syncfusion vertical SfMenu (ShowItemOnClick) parent *toggles* on
    /// click, so if the group was already open (e.g. a prior HasGroupedMenuItemAsync left it that
    /// way) a naive click would collapse it — this checks the child first and, if a click closes
    /// rather than opens it, clicks once more to land on "expanded".
    /// </summary>
    private async Task EnsureGroupExpandedAsync(string groupText, string itemText)
    {
        var child = ChildItem(itemText).First;
        if (await child.IsVisibleAsync())
            return;

        var group = GroupHeader(groupText).First;
        await group.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await group.ClickAsync();

        try
        {
            await child.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
        }
        catch (TimeoutException)
        {
            await group.ClickAsync();
            await child.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        }
    }

    /// <summary>
    /// Expands the named top-level nav group and returns true if it now shows a child item with
    /// exactly <paramref name="itemText"/>.
    /// </summary>
    public async Task<bool> HasGroupedMenuItemAsync(string groupText, string itemText)
    {
        try
        {
            await EnsureGroupExpandedAsync(groupText, itemText);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    /// <summary>Expands the named group (if not already) and clicks its child item. Caller waits for the navigation.</summary>
    public async Task ClickGroupedMenuItemAsync(string groupText, string itemText)
    {
        await EnsureGroupExpandedAsync(groupText, itemText);
        await ChildItem(itemText).First.ClickAsync();
    }
}
