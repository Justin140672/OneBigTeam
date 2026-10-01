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
        const int attempts = 5;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            if (await HelpDropdown.IsVisibleAsync())
                return;

            await HelpButton.ClickAsync();
            try
            {
                await HelpDropdown.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = attempt < attempts ? 4_000 : 10_000 });
                return;
            }
            catch (TimeoutException) when (attempt < attempts)
            {
            }
        }
    }

    public Task ClickGettingStartedAsync() =>
        HelpDropdown.GetByRole(AriaRole.Link, new() { Name = "Getting Started" }).ClickAsync();
}
