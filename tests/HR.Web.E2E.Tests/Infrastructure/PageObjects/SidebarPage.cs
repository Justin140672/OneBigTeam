using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class SidebarPage(IPage page)
{
    private ILocator NavMenu => page.Locator(".app-nav-menu");

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

    public async Task<bool> HasTopLevelMenuItemAsync(string text)
    {
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

    public async Task ClickTopLevelMenuItemAsync(string text) =>
        await NavMenu.GetByText(text, new() { Exact = true }).ClickAsync();


    private ILocator GroupHeader(string text) =>
        NavMenu.GetByText(text, new() { Exact = true });

    private ILocator ChildItem(string text) =>
        page.GetByText(text, new() { Exact = true });

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

    public async Task ClickGroupedMenuItemAsync(string groupText, string itemText)
    {
        await EnsureGroupExpandedAsync(groupText, itemText);
        await ChildItem(itemText).First.ClickAsync();
    }
}
