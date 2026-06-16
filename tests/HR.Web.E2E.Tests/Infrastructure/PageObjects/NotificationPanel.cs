using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class NotificationPanel(IPage page)
{
    /// <summary>Returns the unread count shown on the bell badge, or 0 if no badge is visible.</summary>
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
    }

    public async Task CloseAsync()
    {
        // Click the bell again to toggle closed, or click away.
        await page.Locator(".notif-btn").ClickAsync();
    }

    /// <summary>Returns all notification titles currently in the open dropdown.</summary>
    public async Task<IReadOnlyList<string>> GetNotificationTitlesAsync()
    {
        var items = await page.Locator(".notif-item-title").AllAsync();
        var titles = new List<string>();
        foreach (var item in items)
            titles.Add((await item.TextContentAsync())?.Trim() ?? "");
        return titles;
    }

    /// <summary>
    /// Clicks the first notification whose title contains <paramref name="titleFragment"/>
    /// and waits for navigation to the task view page.
    /// </summary>
    public async Task ClickNotificationAsync(string titleFragment)
    {
        var item = page.Locator(".notif-item")
            .Filter(new() { HasText = titleFragment })
            .First;

        await item.ClickAsync();
        await page.WaitForURLAsync(new Regex("/tasks/"), new() { Timeout = 15_000 });
    }
}
