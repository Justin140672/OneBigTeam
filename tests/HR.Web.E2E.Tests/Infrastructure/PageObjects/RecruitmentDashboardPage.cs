using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class RecruitmentDashboardPage(IPage page, string baseUrl)
{
    public async Task GoToAsync()
    {
        await page.GotoAsync($"{baseUrl}/dashboard/recruitment");
        await page.WaitForSelectorAsync(".recruitment-dashboard-header", new() { Timeout = 20_000 });
    }


    public Task<string> GetHeaderTitleAsync() =>
        page.Locator(".recruitment-dashboard-header h1.dashboard-heading").TextContentAsync()!;

    public async Task<string> GetHeaderSummaryAsync() =>
        (await page.Locator(".recruitment-dashboard-header p.text-muted").TextContentAsync())?.Trim() ?? "";

    public async Task WaitForSummaryTilesLoadedAsync() =>
        await page.Locator(".widget-kpi-row[role='list']").WaitForAsync(new() { Timeout = 15_000 });

    public async Task<int> GetSummaryTileValueAsync(string label)
    {
        await WaitForSummaryTilesLoadedAsync();

        var tile = page.Locator(".widget-kpi-row[role='list'] .widget-kpi")
            .Filter(new() { HasText = label })
            .First;

        var text = (await tile.Locator(".widget-kpi-value").TextContentAsync())?.Trim() ?? "0";
        return int.Parse(text);
    }


    public enum Tab { Pipeline, Activity, Insights }

    private ILocator TabButton(Tab tab) => page.Locator($"[data-testid='recruitment-tab-{tab.ToString().ToLowerInvariant()}']");

    public async Task SwitchToTabAsync(Tab tab)
    {
        await TabButton(tab).ClickAsync();
        await Assertions.Expect(TabButton(tab)).ToHaveClassAsync(new Regex("active"), new() { Timeout = 10_000 });
    }

    public async Task<bool> IsTabActiveAsync(Tab tab) =>
        (await TabButton(tab).GetAttributeAsync("class"))?.Contains("active") ?? false;


    private ILocator VacancyPickerWrapper => page.Locator(".recruitment-dashboard-vacancy-picker");

    public async Task SelectVacancyAsync(string vacancyTitle)
    {
        await VacancyPickerWrapper.WaitForAsync(new() { Timeout = 15_000 });
        await DropDownSelector.SelectAsync(page, VacancyPickerWrapper, vacancyTitle);
    }

    public async Task FillBoardSearchAsync(string text)
    {
        var input = page.Locator("input[data-testid='kanban-search-box'], [data-testid='kanban-search-box'] input").First;
        await input.FillAsync(text);
        await input.PressAsync("Tab");
        await page.WaitForTimeoutAsync(400);
    }

    private ILocator ShowClosedCandidatesCheckbox => page.Locator("#show-terminal-stages");

    public Task<bool> IsShowClosedCandidatesCheckedAsync() => ShowClosedCandidatesCheckbox.IsCheckedAsync();

    public async Task ToggleShowClosedCandidatesAsync()
    {
        await ShowClosedCandidatesCheckbox.ClickAsync();
        await page.WaitForTimeoutAsync(300);
    }

    public async Task SwitchToBoardViewAsync()
    {
        var button = page.Locator("[data-testid='recruitment-view-board-btn']");
        await button.ClickAsync();
        await Assertions.Expect(button).ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = 10_000 });
    }

    public async Task SwitchToListViewAsync()
    {
        var button = page.Locator("[data-testid='recruitment-view-list-btn']");
        await button.ClickAsync();
        await Assertions.Expect(button).ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = 10_000 });
    }


    public Task ClickCreateVacancyAsync() => page.Locator("[data-testid='recruitment-dashboard-create-vacancy-btn']").ClickAsync();

    public Task ClickAddCandidateAsync() => page.Locator("[data-testid='recruitment-dashboard-add-candidate-btn']").ClickAsync();

    private ILocator WidgetCard(string widgetTitle) =>
        page.Locator(".widget-card")
            .Filter(new() { Has = page.Locator(".widget-header .widget-title", new() { HasText = widgetTitle }) })
            .First;

    public async Task<bool> HasWidgetAsync(string widgetTitle)
    {
        await EnsureTabForWidgetAsync(widgetTitle);

        try
        {
            await WidgetCard(widgetTitle).Locator(".widget-header")
                .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task WaitForWidgetLoadedAsync(string widgetTitle)
    {
        await EnsureTabForWidgetAsync(widgetTitle);
        var widget = WidgetCard(widgetTitle);
        await widget.Locator(".task-widget-item, .widget-empty").First.WaitForAsync(new() { Timeout = 15_000 });
    }

    private static readonly string[] InsightsWidgetTitles = ["Hiring Pipeline", "New Hires"];

    private async Task EnsureTabForWidgetAsync(string widgetTitle)
    {
        var targetTab = InsightsWidgetTitles.Any(t => widgetTitle.Contains(t, StringComparison.OrdinalIgnoreCase))
            ? Tab.Insights
            : Tab.Activity;

        if (!await IsTabActiveAsync(targetTab))
            await SwitchToTabAsync(targetTab);
    }


    public async Task WaitForHiringPipelineChartLoadedAsync()
    {
        if (!await IsTabActiveAsync(Tab.Insights))
            await SwitchToTabAsync(Tab.Insights);

        var widget = page.Locator(".widget-card").Filter(new() { HasText = "Hiring Pipeline" });
        await widget.Locator("svg, .widget-empty").First
            .WaitForAsync(new() { Timeout = 15_000 });
    }

    public async Task WaitForNewHiresTrendChartLoadedAsync()
    {
        if (!await IsTabActiveAsync(Tab.Insights))
            await SwitchToTabAsync(Tab.Insights);

        var widget = page.Locator(".widget-card").Filter(new() { HasText = "New Hires" });
        await widget.Locator(".e-chart, .widget-empty").First
            .WaitForAsync(new() { Timeout = 15_000 });
    }


    private ILocator RecruitmentWidget =>
        page.Locator(".widget-card").Filter(new() { HasText = "Recruitment" }).First;

    public async Task WaitForRecruitmentWidgetLoadedAsync()
    {
        if (!await IsTabActiveAsync(Tab.Activity))
            await SwitchToTabAsync(Tab.Activity);

        await RecruitmentWidget.Locator(".widget-kpi-row").WaitForAsync(new() { Timeout = 15_000 });
    }

    public async Task<int> GetRecruitmentKpiValueAsync(string label)
    {
        await WaitForRecruitmentWidgetLoadedAsync();

        var kpi = RecruitmentWidget.Locator(".widget-kpi")
            .Filter(new() { HasText = label })
            .First;

        var text = (await kpi.Locator(".widget-kpi-value").TextContentAsync())?.Trim() ?? "0";
        return int.Parse(text);
    }

    public async Task ClickOpenVacanciesKpiAsync()
    {
        await WaitForRecruitmentWidgetLoadedAsync();

        await RecruitmentWidget.Locator(".widget-kpi")
            .Filter(new() { HasText = "Open Vacancies" })
            .First
            .ClickAsync();

        await page.WaitForURLAsync(new Regex("/vacancies"), new() { Timeout = 30_000 });
    }


    public async Task<IReadOnlyList<string>> GetUpcomingInterviewCandidateNamesAsync()
    {
        if (!await IsTabActiveAsync(Tab.Activity))
            await SwitchToTabAsync(Tab.Activity);

        var widget = page.Locator(".widget-card").Filter(new() { HasText = "Upcoming Interviews" }).First;
        await widget.Locator(".task-widget-item, .widget-empty").First.WaitForAsync(new() { Timeout = 15_000 });

        var titles = await widget.Locator(".task-widget-title").AllAsync();
        var names  = new List<string>();
        foreach (var t in titles)
            names.Add((await t.TextContentAsync())?.Trim() ?? "");
        return names;
    }


    private ILocator DrillDownDialog => page.Locator("[role='dialog'].recruitment-metric-drilldown-dialog");

    private ILocator SummaryTile(string label) =>
        page.Locator(".widget-kpi-row[role='list'] .widget-kpi").Filter(new() { HasText = label }).First;

    public async Task OpenMetricDrillDownAsync(string label)
    {
        await WaitForSummaryTilesLoadedAsync();
        await WaitForOverlayToClearAsync();
        await SummaryTile(label).ClickAsync();
        await DrillDownDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    public async Task<int> GetDrillDownRowCountAsync()
    {
        await DrillDownDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });

        var emptyState = DrillDownDialog.Locator("p.text-muted").Filter(new() { HasText = "Nothing to show" });
        if (await emptyState.IsVisibleAsync())
            return 0;

        await DrillDownDialog.Locator(".e-grid .e-row, .e-grid .e-emptyrow").First
            .WaitForAsync(new() { Timeout = 15_000 });

        if (await DrillDownDialog.Locator(".e-grid .e-emptyrow").IsVisibleAsync())
            return 0;

        return await DrillDownDialog.Locator(".e-grid .e-row").CountAsync();
    }

    public Task<bool> IsMetricDrillDownOpenAsync() => DrillDownDialog.IsVisibleAsync();

    public async Task CloseMetricDrillDownAsync()
    {
        await DrillDownDialog.GetByRole(AriaRole.Button, new() { Name = "Close" }).First.ClickAsync();
        await DrillDownDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
        await WaitForOverlayToClearAsync();
    }

    private async Task WaitForOverlayToClearAsync()
    {
        try
        {
            await page.Locator(".e-dlg-overlay").WaitForAsync(
                new() { State = WaitForSelectorState.Hidden, Timeout = 5_000 });
        }
        catch (TimeoutException)
        {
        }
    }
}
