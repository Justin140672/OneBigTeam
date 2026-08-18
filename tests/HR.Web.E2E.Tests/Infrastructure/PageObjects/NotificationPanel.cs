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

        // The dropdown container mounting doesn't prove its notification list has finished
        // loading (MainLayout.razor fetches the list async and shows either real ".notif-item"
        // rows or the ".notif-empty" placeholder once done) — same "container before content"
        // race fixed elsewhere in this suite. A caller that immediately calls
        // GetNotificationTitlesAsync() can otherwise read an empty list for a persona who
        // genuinely has notifications.
        await page.WaitForSelectorAsync(".notif-item, .notif-empty", new() { Timeout = 10_000 });
    }

    public async Task CloseAsync()
    {
        await page.Locator(".notif-btn").ClickAsync();
        await page.WaitForSelectorAsync(".notif-dropdown",
            new() { State = WaitForSelectorState.Hidden, Timeout = 5_000 });
    }

    /// <summary>
    /// Clicks "Mark all read" inside an already-open notification panel and waits
    /// for the unread badge to be removed from the DOM.
    /// </summary>
    public async Task MarkAllReadAsync()
    {
        await page.Locator(".notif-mark-all").ClickAsync();
        // Badge is conditionally rendered (@if unreadCount > 0); wait for it to vanish.
        await page.WaitForFunctionAsync(
            "!document.querySelector('.notif-badge')",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
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
    /// Clicks the first notification whose title contains <paramref name="titleFragment"/>.
    /// This opens TaskViewDialog in place (no navigation) — use TaskViewPage.WaitForLoadedAsync
    /// (or its own methods, which wait internally) to read the opened task's content.
    /// </summary>
    public async Task ClickNotificationAsync(string titleFragment)
    {
        var item = page.Locator(".notif-item")
            .Filter(new() { HasText = titleFragment })
            .First;

        await item.ClickAsync();
        await page.WaitForSelectorAsync(".task-view-dialog", new() { Timeout = 15_000 });
    }
}
