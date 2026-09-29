using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class HelpMenu(IPage page)
{
    private ILocator HelpButton => page.Locator(".help-btn");
    private ILocator HelpDropdown => page.Locator(".help-dropdown");

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
