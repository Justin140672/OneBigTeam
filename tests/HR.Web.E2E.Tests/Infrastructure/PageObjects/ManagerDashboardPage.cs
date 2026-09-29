using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class ManagerDashboardPage(IPage page, string baseUrl)
{
    public async Task GoToAsync()
    {
        await page.GotoAsync($"{baseUrl}/dashboard/manager");
        await page.WaitForSelectorAsync(".dashboard-greeting", new() { Timeout = 35_000 });
    }

    public async Task<bool> HasWidgetAsync(string widgetTitle)
    {
        try
        {
            await page.Locator(".widget-header")
                .Filter(new() { HasText = widgetTitle })
                .First
                .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task WaitForWidgetLoadedAsync(string widgetTitle)
    {
        var widget = page.Locator(".widget-card").Filter(new() { HasText = widgetTitle }).First;
        await widget.Locator(".task-widget-item, .widget-empty, .attention-queue-all-clear").First.WaitForAsync(new() { Timeout = 15_000 });
    }


    private ILocator AttentionQueueWidget =>
        page.Locator(".widget-card.attention-queue-card").First;

    public async Task WaitForAttentionQueueLoadedAsync() =>
        await AttentionQueueWidget.Locator(".attention-queue-item, .attention-queue-all-clear").First
            .WaitForAsync(new() { Timeout = 15_000 });

    public async Task<IReadOnlyList<string>> GetAttentionQueueSubjectsAsync(string? categoryFilter = null)
    {
        await WaitForAttentionQueueLoadedAsync();

        var rows = categoryFilter is null
            ? AttentionQueueWidget.Locator(".attention-queue-item")
            : AttentionQueueWidget.Locator(".attention-queue-item").Filter(new() { HasText = categoryFilter });

        var titles = await rows.Locator(".task-widget-title").AllAsync();
        var names  = new List<string>();
        foreach (var t in titles)
            names.Add((await t.TextContentAsync())?.Trim() ?? "");
        return names;
    }

    public async Task<IReadOnlyList<string>> GetAttentionQueueEmployeeNamesAsync(string? categoryFilter = null)
    {
        await WaitForAttentionQueueLoadedAsync();

        var rows = categoryFilter is null
            ? AttentionQueueWidget.Locator(".attention-queue-item")
            : AttentionQueueWidget.Locator(".attention-queue-item").Filter(new() { HasText = categoryFilter });

        var metas = await rows.Locator(".task-widget-meta").AllAsync();
        var names = new List<string>();
        foreach (var m in metas)
            names.Add((await m.TextContentAsync())?.Trim() ?? "");
        return names;
    }

    public async Task<bool> IsAttentionQueueItemOverdueAsync(string subjectFragment)
    {
        await WaitForAttentionQueueLoadedAsync();
        var row = AttentionQueueWidget.Locator(".attention-queue-item").Filter(new() { HasText = subjectFragment }).First;
        var classes = await row.GetAttributeAsync("class") ?? "";
        return classes.Contains("attention-queue-item--overdue");
    }

    public async Task<bool> AttentionQueueIsAllClearAsync() =>
        await AttentionQueueWidget.Locator(".attention-queue-all-clear").IsVisibleAsync();


    public async Task<int> GetAttentionQueueCountBadgeAsync()
    {
        var badge = AttentionQueueWidget.Locator(".widget-count-badge").First;
        if (!await badge.IsVisibleAsync())
            return 0;
        var text = (await badge.TextContentAsync())?.Trim();
        return int.TryParse(text, out var value) ? value : 0;
    }

    public async Task<int> GetAttentionQueueRowCountAsync()
    {
        await WaitForAttentionQueueLoadedAsync();
        return await AttentionQueueWidget.Locator(".attention-queue-item").CountAsync();
    }

    public async Task<int> GetAttentionQueueSourceWarningCountAsync() =>
        await AttentionQueueWidget.Locator(".widget-source-warning").CountAsync();

    public async Task<bool> HasAttentionQueueSourceWarningAsync(string sourceName) =>
        await AttentionQueueWidget.Locator(".widget-source-warning")
            .Filter(new() { HasText = sourceName }).First.IsVisibleAsync();

    public async Task RetryAttentionQueueAllAsync() =>
        await AttentionQueueWidget.Locator(".widget-source-warning .widget-source-warning-retry")
            .First.ClickAsync();

    public async Task WaitForAttentionQueueSourceWarningsClearedAsync() =>
        await AttentionQueueWidget.Locator(".widget-source-warning").First
            .WaitForAsync(new() { State = WaitForSelectorState.Detached, Timeout = 20_000 });

    public async Task ClickAttentionQueueItemAsync(string textFragment)
    {
        await WaitForAttentionQueueLoadedAsync();
        await AttentionQueueWidget.Locator(".attention-queue-item")
            .Filter(new() { HasText = textFragment })
            .First
            .ClickAsync();
    }

    public async Task ClickAttentionQueueItemAsync(string textFragment, string categoryFragment)
    {
        await WaitForAttentionQueueLoadedAsync();
        await AttentionQueueWidget.Locator(".attention-queue-item")
            .Filter(new() { HasText = textFragment })
            .Filter(new() { HasText = categoryFragment })
            .First
            .ClickAsync();
    }


    private ILocator TeamStatusWidget =>
        page.Locator(".widget-card.team-status-summary").First;

    public async Task WaitForTeamStatusLoadedAsync() =>
        await TeamStatusWidget.Locator(".team-status-tile, .widget-empty").First
            .WaitForAsync(new() { Timeout = 15_000 });

    public async Task<int> GetTeamStatusValueAsync(string tileLabel)
    {
        await WaitForTeamStatusLoadedAsync();
        var tile = TeamStatusWidget.Locator(".team-status-tile")
            .Filter(new() { Has = page.Locator(".team-status-label", new() { HasText = tileLabel }) })
            .First;
        var text = await tile.Locator(".team-status-value").TextContentAsync();
        return int.TryParse(text?.Trim(), out var value) ? value : 0;
    }

    public async Task<int> GetTeamStatusHeaderCountAsync()
    {
        await WaitForTeamStatusLoadedAsync();
        var text = await TeamStatusWidget.Locator(".widget-count-badge").First.TextContentAsync();
        return int.TryParse(text?.Trim(), out var value) ? value : -1;
    }

    public async Task<bool> TeamStatusIsEmptyAsync() =>
        await TeamStatusWidget.Locator(".widget-empty").Filter(new() { HasText = "No one reports up to you yet" }).IsVisibleAsync();

    private ILocator TeamStatusTile(string tileLabel) =>
        TeamStatusWidget.Locator(".team-status-tile")
            .Filter(new() { Has = page.GetByText(tileLabel, new() { Exact = true }) })
            .First;

    public async Task<IReadOnlyList<string>> GetTeamStatusTileLabelsAsync()
    {
        await WaitForTeamStatusLoadedAsync();
        var labels = await TeamStatusWidget.Locator(".team-status-tile .team-status-label").AllAsync();
        var result = new List<string>();
        foreach (var l in labels)
            result.Add((await l.TextContentAsync())?.Trim() ?? "");
        return result;
    }

    public async Task<string> GetTeamStatusTileTagNameAsync(string tileLabel) =>
        (await TeamStatusTile(tileLabel).EvaluateAsync<string>("el => el.tagName.toLowerCase()")) ?? "";

    public async Task<bool> TeamStatusTileIsKeyboardFocusableAsync(string tileLabel)
    {
        var tile = TeamStatusTile(tileLabel);
        await tile.FocusAsync();
        return await tile.EvaluateAsync<bool>("el => el === document.activeElement");
    }

    public async Task<bool> TeamStatusTileIsExpandedAsync(string tileLabel) =>
        (await TeamStatusTile(tileLabel).GetAttributeAsync("aria-expanded")) == "true";

    public async Task ClickTeamStatusTileAsync(string tileLabel)
    {
        await WaitForTeamStatusLoadedAsync();
        var tile = TeamStatusTile(tileLabel);
        var wasExpanded = await tile.GetAttributeAsync("aria-expanded") == "true";
        await tile.ClickAsync();

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (await tile.GetAttributeAsync("aria-expanded") == (wasExpanded ? "true" : "false")
               && DateTime.UtcNow < deadline)
        {
            await page.WaitForTimeoutAsync(100);
        }
    }

    public async Task<IReadOnlyList<string>> GetTeamStatusDrilldownNamesAsync()
    {
        var panel = TeamStatusWidget.Locator(".team-status-drilldown");
        if (!await panel.IsVisibleAsync())
            return [];

        var names = await panel.Locator(".team-status-drilldown-name").AllAsync();
        var result = new List<string>();
        foreach (var n in names)
            result.Add((await n.TextContentAsync())?.Trim() ?? "");
        return result;
    }

    public async Task<bool> TeamStatusHasSourceWarningAsync() =>
        await TeamStatusWidget.Locator(".widget-source-warning").IsVisibleAsync();


    private ILocator MyTeamWidget =>
        page.Locator(".widget-card").Filter(new() { HasText = "My Team" }).First;

    public async Task<IReadOnlyList<string>> GetMyTeamMemberNamesAsync()
    {
        await MyTeamWidget.Locator(".team-card, .widget-empty").First.WaitForAsync(new() { Timeout = 15_000 });

        var titles = await MyTeamWidget.Locator(".team-card-name").AllAsync();
        var names  = new List<string>();
        foreach (var t in titles)
            names.Add((await t.TextContentAsync())?.Trim() ?? "");
        return names;
    }

    public async Task<string?> GetTeamMemberPhoneFromLinkAsync(string nameFragment)
    {
        var card = MyTeamWidget.Locator(".team-card").Filter(new() { HasText = nameFragment }).First;
        var telLink = card.Locator("a.team-card-contact-link[href^='tel:']");
        if (await telLink.CountAsync() == 0)
            return null;

        var href = await telLink.First.GetAttributeAsync("href");
        return href is null ? null : href["tel:".Length..].Trim();
    }

    public async Task<IReadOnlyList<string>> GetTeamMemberContactTextAsync(string nameFragment)
    {
        var card = MyTeamWidget.Locator(".team-card").Filter(new() { HasText = nameFragment }).First;

        await card.Locator(".team-card-contact-text").First.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        var spans = await card.Locator(".team-card-contact-text").AllAsync();

        var values = new List<string>();
        foreach (var s in spans)
            values.Add((await s.TextContentAsync())?.Trim() ?? "");
        return values;
    }

    public async Task<string> GetTeamMemberStatusAsync(string nameFragment)
    {
        var card = MyTeamWidget.Locator(".team-card").Filter(new() { HasText = nameFragment }).First;
        return (await card.Locator(".team-card-status").TextContentAsync())?.Trim() ?? "";
    }

    public async Task ClickNotifySicknessForTeamMemberAsync(string nameFragment)
    {
        var card = MyTeamWidget.Locator(".team-card").Filter(new() { HasText = nameFragment }).First;
        await card.GetByRole(AriaRole.Button, new() { Name = "Notify Sickness" }).ClickAsync();
        await page.WaitForSelectorAsync("[role='dialog'].record-sickness-dialog", new() { Timeout = 10_000 });
    }

    public async Task ClickViewAllTeamAsync() =>
        await MyTeamWidget.Locator("[data-testid='view-all-team-link']").ClickAsync();

    public async Task<bool> HasViewAllTeamInlineLinkAsync() =>
        await MyTeamWidget.Locator("[data-testid='view-all-team-inline-link']").IsVisibleAsync();

    public async Task ClickViewProfileForTeamMemberAsync(string nameFragment)
    {
        var card = MyTeamWidget.Locator(".team-card").Filter(new() { HasText = nameFragment }).First;
        await card.GetByRole(AriaRole.Button, new() { Name = "View profile" }).ClickAsync();
    }
}
