using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class NotificationPanel(IPage page)
{
    public async Task<int> GetUnreadCountAsync()
    {
        var badge = page.Locator(".notif-badge");
        if (!await badge.IsVisibleAsync()) return 0;
        var text = (await badge.TextContentAsync())?.Trim() ?? "0";
        return text == "99+" ? 99 : int.TryParse(text, out var n) ? n : 0;
    }

    public async Task OpenAsync()
    {
        await page.Locator(".notif-btn").ClickAsync();
        await page.WaitForSelectorAsync(".notif-dropdown", new() { Timeout = 10_000 });

        await page.Locator(".notif-loading").WaitForAsync(
            new() { State = WaitForSelectorState.Detached, Timeout = 15_000 });
        await page.WaitForSelectorAsync(".notif-item, .notif-empty", new() { Timeout = 10_000 });
    }

    public async Task CloseAsync()
    {
        await page.Locator(".notif-btn").ClickAsync();
        await page.WaitForSelectorAsync(".notif-dropdown",
            new() { State = WaitForSelectorState.Hidden, Timeout = 5_000 });
    }

    public async Task MarkAllReadAsync()
    {
        await page.Locator(".notif-mark-all").ClickAsync();
        await page.WaitForFunctionAsync(
            "!document.querySelector('.notif-badge')",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
    }

    public async Task<IReadOnlyList<string>> GetNotificationTitlesAsync()
    {
        var items = await page.Locator(".notif-item-title").AllAsync();
        var titles = new List<string>();
        foreach (var item in items)
            titles.Add((await item.TextContentAsync())?.Trim() ?? "");
        return titles;
    }

    public async Task ClickNotificationAsync(string titleFragment)
    {
        var item = page.Locator(".notif-item")
            .Filter(new() { HasText = titleFragment })
            .First;

        await item.ClickAsync();
        await page.WaitForSelectorAsync(".task-view-dialog", new() { Timeout = 15_000 });
    }
}
