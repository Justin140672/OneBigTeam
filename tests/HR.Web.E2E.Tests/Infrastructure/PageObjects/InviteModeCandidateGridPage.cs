using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class InviteModeCandidateGridPage(IPage page)
{
    private ILocator Root => page.Locator(".invite-mode-candidate-grid");

    private const string RowsRenderedSelector =
        ".invite-mode-grid .e-row, .invite-mode-grid .e-emptyrow";

    public async Task WaitForLoadedAsync()
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 20_000 });

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (await GetSelectedCountAsync() == 0 && DateTime.UtcNow < deadline)
            await page.WaitForTimeoutAsync(100);
    }

    private ILocator RowOnCurrentPage(string nameFragment) =>
        page.Locator(".invite-mode-grid .e-row").Filter(new() { HasText = nameFragment }).First;

    private ILocator FirstRow => page.Locator(".invite-mode-grid .e-row").First;

    private async Task<ILocator> Row(string nameFragment)
    {
        var row = RowOnCurrentPage(nameFragment);
        if (await row.CountAsync() > 0)
            return row;

        await ClickPagerAndWaitAsync(page.Locator(".invite-mode-grid .e-pager .e-first:not(.e-disable)"));

        for (var pageIndex = 1; pageIndex <= 50; pageIndex++)
        {
            if (await row.CountAsync() > 0)
                return row;

            if (!await ClickPagerAndWaitAsync(page.Locator(".invite-mode-grid .e-pager .e-next:not(.e-disable)")))
                break;
        }

        return row;
    }

    private async Task<bool> ClickPagerAndWaitAsync(ILocator pagerControl)
    {
        if (await pagerControl.CountAsync() == 0)
            return false;

        var firstRowBefore = await FirstRow.InnerTextAsync();
        await pagerControl.First.ClickAsync();

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline &&
               (await FirstRow.CountAsync() == 0 || await FirstRow.InnerTextAsync() == firstRowBefore))
        {
            await page.WaitForTimeoutAsync(100);
        }

        return true;
    }

    public async Task<bool> IsRowCheckedAsync(string nameFragment)
    {
        var row = await Row(nameFragment);
        var input = row.Locator(".e-checkbox-wrapper input[type='checkbox']").First;
        return await input.IsCheckedAsync();
    }

    public async Task ToggleRowAsync(string nameFragment)
    {
        var row = await Row(nameFragment);
        var input = row.Locator(".e-checkbox-wrapper input[type='checkbox']").First;
        var wasChecked = await input.IsCheckedAsync();
        var countBefore = await GetSelectedCountAsync();
        var expectedCount = wasChecked ? countBefore - 1 : countBefore + 1;

        await row.Locator(".e-checkbox-wrapper").First.ClickAsync();

        await WaitForSelectedCountAsync(count => count == expectedCount);
    }

    public async Task<bool> ShowsMissingEmailExplanationAsync(string nameFragment) =>
        await (await Row(nameFragment)).GetByText("No work email", new() { Exact = false }).IsVisibleAsync();

    public async Task<string?> GetMissingEmailEditLinkHrefAsync(string nameFragment) =>
        await (await Row(nameFragment)).GetByRole(AriaRole.Link).Last.GetAttributeAsync("href");

    public async Task<int> GetSelectedCountAsync()
    {
        var text = (await Root.Locator("span.text-muted").First.InnerTextAsync()).Trim();
        var digits = new string(text.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var value) ? value : 0;
    }

    public ILocator SelectAllEligibleButton => Root.GetByRole(AriaRole.Button, new() { Name = "Select all eligible" });
    public ILocator ClearSelectionButton => Root.GetByRole(AriaRole.Button, new() { Name = "Clear selection" });

    public async Task ClickSelectAllEligibleAsync()
    {
        await SelectAllEligibleButton.ClickAsync();
        await WaitForSelectedCountAsync(count => count > 0);
    }

    public async Task ClickClearSelectionAsync()
    {
        await ClearSelectionButton.ClickAsync();
        await WaitForSelectedCountAsync(count => count == 0);
    }

    private async Task WaitForSelectedCountAsync(Func<int, bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition(await GetSelectedCountAsync()) && DateTime.UtcNow < deadline)
            await page.WaitForTimeoutAsync(100);
    }

    public ILocator InviteSelectedButton => Root.Locator("#invite-mode-invite-selected-btn");

    public Task<bool> IsInviteSelectedButtonDisabledAsync() => InviteSelectedButton.IsDisabledAsync();

    public Task ClickInviteSelectedAsync() => InviteSelectedButton.ClickAsync();
}
