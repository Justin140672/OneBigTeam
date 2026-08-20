using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Interacts with the Help menu in MainLayout.razor's top bar — a "?" button
/// (".help-btn", inside ".help-menu") only rendered for HR Administrator / Company
/// Administrator personas, that toggles open a dropdown (".help-dropdown") with links
/// (".help-item"): "Getting Started" (/getting-started) and "Help & Feedback" (/support).
/// </summary>
public sealed class HelpMenu(IPage page)
{
    private ILocator HelpButton => page.Locator(".help-btn");
    private ILocator HelpDropdown => page.Locator(".help-dropdown");

    /// <summary>
    /// True if the Help "?" button is rendered at all — false for a persona without HR
    /// Administrator / Company Administrator rights (MainLayout.razor's role gate around
    /// ".help-menu"). Uses a bounded wait rather than an instant check since the gating flags
    /// (Session.IsHrAdministrator / Session.CanManageCompany) only become accurate after
    /// AppSession's async /api/me round-trip completes — mirrors SidebarPage.IsSidebarVisibleAsync.
    /// </summary>
    public async Task<bool> IsVisibleAsync()
    {
        try
        {
            await HelpButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task OpenAsync()
    {
        await HelpButton.ClickAsync();
        await HelpDropdown.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    public Task ClickGettingStartedAsync() =>
        HelpDropdown.GetByRole(AriaRole.Link, new() { Name = "Getting Started" }).ClickAsync();
}
