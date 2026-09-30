using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class HrDashboardPage(IPage page, string baseUrl)
{
    public async Task GoToAsync()
    {
        await page.GotoAsync($"{baseUrl}/dashboard/hr");
        await page.WaitForSelectorAsync(".dashboard-greeting", new() { Timeout = 20_000 });
    }

    public async Task<bool> HasWidgetAsync(string widgetTitle) =>
        await page.Locator(".widget-header")
            .Filter(new() { HasText = widgetTitle })
            .IsVisibleAsync();

    public async Task WaitForWidgetLoadedAsync(string widgetTitle)
    {
        var widget = page.Locator(".widget-card").Filter(new() { HasText = widgetTitle }).First;
        await widget.Locator(".task-widget-item, .widget-empty").First.WaitForAsync(new() { Timeout = 15_000 });
    }


    private ILocator HeadcountWidget =>
        page.Locator(".widget-card").Filter(new() { HasText = "Headcount by Department" }).First;

    public async Task WaitForHeadcountChartLoadedAsync() =>
        await HeadcountWidget.Locator(".hbar-row--button, .widget-empty").First
            .WaitForAsync(new() { Timeout = 15_000 });

    public async Task<IReadOnlyList<string>> GetHeadcountDepartmentLabelsAsync()
    {
        await WaitForHeadcountChartLoadedAsync();
        var labels = await HeadcountWidget.Locator(".hbar-row--button .hbar-label").AllAsync();
        var names  = new List<string>();
        foreach (var l in labels)
            names.Add((await l.TextContentAsync())?.Trim() ?? "");
        return names;
    }

    public async Task ClickHeadcountViewAllEmployeesAsync()
    {
        await HeadcountWidget.GetByRole(AriaRole.Link, new() { Name = "View all employees" }).ClickAsync();
        await page.WaitForURLAsync(new Regex(@"/companies/[0-9a-f-]{36}/employees$"), new() { Timeout = 15_000 });
    }


    private ILocator GenderSplitWidget =>
        page.Locator(".widget-card").Filter(new() { HasText = "Gender Split" }).First;

    public async Task WaitForGenderSplitChartLoadedAsync() =>
        await GenderSplitWidget.Locator(".hbar-chart, .widget-empty").First
            .WaitForAsync(new() { Timeout = 15_000 });

    public async Task<bool> GenderSplitChartIsEmptyAsync() =>
        await GenderSplitWidget.Locator(".widget-empty").IsVisibleAsync();

    public async Task<IReadOnlyList<string>> GetGenderSplitLabelsAsync()
    {
        await WaitForGenderSplitChartLoadedAsync();
        var labels = await GenderSplitWidget.Locator(".hbar-label").AllAsync();
        var names  = new List<string>();
        foreach (var l in labels)
            names.Add((await l.TextContentAsync())?.Trim() ?? "");
        return names;
    }


    private ILocator EmploymentTypeSplitWidget =>
        page.Locator(".widget-card").Filter(new() { HasText = "Employment Type" }).First;

    public async Task WaitForEmploymentTypeSplitChartLoadedAsync() =>
        await EmploymentTypeSplitWidget.Locator(".hbar-chart, .widget-empty").First
            .WaitForAsync(new() { Timeout = 15_000 });

    public async Task<bool> EmploymentTypeSplitChartIsEmptyAsync() =>
        await EmploymentTypeSplitWidget.Locator(".widget-empty").IsVisibleAsync();

    public async Task<IReadOnlyList<string>> GetEmploymentTypeSplitLabelsAsync()
    {
        await WaitForEmploymentTypeSplitChartLoadedAsync();
        var labels = await EmploymentTypeSplitWidget.Locator(".hbar-label").AllAsync();
        var names  = new List<string>();
        foreach (var l in labels)
            names.Add((await l.TextContentAsync())?.Trim() ?? "");
        return names;
    }

    public async Task<IReadOnlyList<LayoutRect>> GetAnalyticsGridTileBoundsAsync()
    {
        var tiles = page.Locator(".dashboard-analytics-grid > .widget-card, .dashboard-analytics-grid > .chart-tile");

        // The dashboard has several other widgets ABOVE the analytics grid (attention queue,
        // favourite reports, recent changes, sickness widgets, ...) that load asynchronously and
        // each shift the analytics grid's Y position as their own content grows in. Waiting for
        // just these three charts' own content to load isn't enough — the page's total layout can
        // still be settling above them at that exact moment, which is what produced wildly
        // different/huge Y readings between runs (thousands of px, and not matching each other)
        // rather than a genuine "not in the same row" bug. Poll until two consecutive reads agree
        // on every tile's Y position (within a small tolerance) before trusting the measurement.
        IReadOnlyList<LayoutRect>? previous = null;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var count = await tiles.CountAsync();
            var bounds = new List<LayoutRect>();
            for (var i = 0; i < count; i++)
            {
                var box = await tiles.Nth(i).BoundingBoxAsync();
                if (box is not null)
                    bounds.Add(new LayoutRect(box.X, box.Y, box.Width, box.Height));
            }

            if (previous is not null
                && previous.Count == bounds.Count
                && previous.Zip(bounds, (p, b) => Math.Abs(p.Y - b.Y) < 1).All(same => same))
            {
                return bounds;
            }

            previous = bounds;
            await page.WaitForTimeoutAsync(250);
        }

        return previous ?? [];
    }

    public readonly record struct LayoutRect(float X, float Y, float Width, float Height);


    private ILocator AttentionQueueWidget =>
        page.Locator(".widget-card.attention-queue-card").First;

    public async Task WaitForAttentionQueueLoadedAsync() =>
        await AttentionQueueWidget.Locator(".attention-queue-item, .attention-queue-all-clear").First
            .WaitForAsync(new() { Timeout = 15_000 });

    public ILocator ActionableAttentionQueueRows => AttentionQueueWidget.Locator("button.attention-queue-item");

    public async Task<IReadOnlyList<string>> GetAttentionQueueSubjectsAsync()
    {
        await WaitForAttentionQueueLoadedAsync();
        var titles = await AttentionQueueWidget.Locator(".attention-queue-item .task-widget-title").AllAsync();
        var names  = new List<string>();
        foreach (var t in titles)
            names.Add((await t.TextContentAsync())?.Trim() ?? "");
        return names;
    }

    public async Task<IReadOnlyList<string>> GetAttentionQueueEmployeeNamesAsync()
    {
        await WaitForAttentionQueueLoadedAsync();
        var metas = await AttentionQueueWidget.Locator(".attention-queue-item .task-widget-meta").AllAsync();
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

    public async Task ClickTaskBackedAttentionQueueItemAsync(string subjectFragment)
    {
        await WaitForAttentionQueueLoadedAsync();
        var taskBackedAction = page.Locator(".attention-queue-action").Filter(new()
        {
            HasTextRegex = new System.Text.RegularExpressions.Regex(
                @"^\s*(Open task|Review leave request|Review probation|Complete return-to-work review|View evidence request)\s*$"),
        });
        await AttentionQueueWidget.Locator(".attention-queue-item")
            .Filter(new() { HasText = subjectFragment })
            .Filter(new() { Has = taskBackedAction })
            .First
            .ClickAsync();
    }

    public async Task ClickAttentionQueueItemAsync(string subjectFragment)
    {
        await WaitForAttentionQueueLoadedAsync();
        await AttentionQueueWidget.Locator(".attention-queue-item")
            .Filter(new() { HasText = subjectFragment })
            .First
            .ClickAsync();
    }


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

    public async Task<IReadOnlyList<bool>> GetAttentionQueueOverdueFlagsAsync()
    {
        await WaitForAttentionQueueLoadedAsync();
        return await ActionableAttentionQueueRows.EvaluateAllAsync<bool[]>(
            "els => els.map(e => e.classList.contains('attention-queue-item--overdue'))");
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


    public async Task ClickDocumentReviewItemAsync(string titleFragment)
    {
        await ClickAttentionQueueItemAsync(titleFragment);
        await page.WaitForURLAsync(
            new Regex(@"/companies/[0-9a-f-]{36}/shared-documents/[0-9a-f-]{36}"),
            new() { Timeout = 15_000 });
    }


    private ILocator FavouriteReportsWidget =>
        page.Locator(".widget-card").Filter(new() { HasText = "Favourite Reports" }).First;

    public async Task<IReadOnlyList<string>> GetFavouriteReportTitlesAsync()
    {
        await FavouriteReportsWidget.Locator(".task-widget-item, .widget-empty").First
            .WaitForAsync(new() { Timeout = 15_000 });

        var titles = await FavouriteReportsWidget.Locator(".task-widget-title").AllAsync();
        var names  = new List<string>();
        foreach (var t in titles)
            names.Add((await t.TextContentAsync())?.Trim() ?? "");
        return names;
    }

    public async Task ClickFavouriteReportItemAsync(string titleFragment)
    {
        await FavouriteReportsWidget.Locator(".task-widget-item, .widget-empty").First
            .WaitForAsync(new() { Timeout = 15_000 });

        await FavouriteReportsWidget.Locator(".task-widget-item")
            .Filter(new() { HasText = titleFragment })
            .First
            .ClickAsync();
        await page.WaitForURLAsync(
            new Regex(@"/companies/[0-9a-f-]{36}/reporting/[a-z-]+"),
            new() { Timeout = 15_000 });
    }

    public async Task ClickFavouriteReportsBrowseAllAsync()
    {
        await FavouriteReportsWidget.Locator(".widget-view-all").ClickAsync();
        await page.WaitForURLAsync(new Regex(@"/companies/[0-9a-f-]{36}/reporting$"), new() { Timeout = 15_000 });
    }
}
