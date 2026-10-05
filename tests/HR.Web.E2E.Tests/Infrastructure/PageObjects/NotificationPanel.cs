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

    public async Task<int> WaitForUnreadAsync(int timeoutMs = 30_000)
    {
        try
        {
            await page.Locator(".notif-badge").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = timeoutMs });
        }
        catch (TimeoutException)
        {
        }

        return await GetUnreadCountAsync();
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

        var dialog = page.Locator(".task-view-dialog");
        const int maxAttempts = 5;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                if (!await item.IsVisibleAsync())
                {
                    await dialog.First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = attempt < maxAttempts ? 8_000 : 30_000 });
                    return;
                }

                await item.ClickAsync(new() { Timeout = attempt < maxAttempts ? 5_000 : 30_000 });
                await dialog.First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = attempt < maxAttempts ? 8_000 : 30_000 });
                return;
            }
            catch (TimeoutException) when (attempt < maxAttempts)
            {
            }
        }
    }
}
