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
    /// Returns true if the sidebar (MainLayout.razor's ShowSidebar) is rendered at all. False for
    /// personas with no qualifying role — a plain Employee, or a plain Manager whose dashboard IS
    /// their home with no separate admin section to reach (see ManagerOnly_SeesNoSidebar... test).
    /// </summary>
    public async Task<bool> IsSidebarVisibleAsync() =>
        await page.Locator(".app-sidebar").CountAsync() > 0;

    /// <summary>
    /// Returns true if a menu item with exactly this text is present and visible without
    /// expanding any parent first — true for top-level items (including the flattened
    /// "Vacancies"/"Candidates" entries and the dashboard links), false for anything still
    /// nested under a collapsed parent (e.g. "Organisation Chart" under "People").
    /// </summary>
    public async Task<bool> HasTopLevelMenuItemAsync(string text) =>
        await NavMenu.GetByText(text, new() { Exact = true }).IsVisibleAsync();

    /// <summary>
    /// Clicks a top-level menu item by its exact text. Does not itself wait for the resulting
    /// navigation — callers should follow up with page.WaitForURLAsync for the expected route.
    /// </summary>
    public async Task ClickTopLevelMenuItemAsync(string text) =>
        await NavMenu.GetByText(text, new() { Exact = true }).ClickAsync();
}
