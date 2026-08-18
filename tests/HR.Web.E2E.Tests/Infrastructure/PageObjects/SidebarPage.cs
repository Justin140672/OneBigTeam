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
}
