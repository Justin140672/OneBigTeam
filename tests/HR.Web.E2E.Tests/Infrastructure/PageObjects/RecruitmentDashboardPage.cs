using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Interacts with the Recruiter-only dashboard
/// (src/HR.Web/Components/Pages/Dashboards/RecruitmentDashboard.razor), reached by navigating
/// directly to "/dashboard/recruitment". The page guards on Session.IsRecruiter and redirects any
/// other role to Session.MyProfileUrl before the widgets below ever render — note this is a
/// stricter gate than the widgets' own internal checks (CanManageEmployees || IsRecruiter): an
/// HR Administrator who is not also a Recruiter can no longer reach this route at all, unlike the
/// pre-restructure single dashboard.
/// </summary>
public sealed class RecruitmentDashboardPage(IPage page, string baseUrl)
{
    public async Task GoToAsync()
    {
        await page.GotoAsync($"{baseUrl}/dashboard/recruitment");
        await page.WaitForSelectorAsync(".recruitment-dashboard-header", new() { Timeout = 20_000 });
    }

    // ── Header / summary tiles (redesign) ────────────────────────────────────

    public Task<string> GetHeaderTitleAsync() =>
        page.Locator(".recruitment-dashboard-header h1.dashboard-heading").TextContentAsync()!;

    /// <summary>The "N open vacancies · M candidates in progress" line under the page title.</summary>
    public async Task<string> GetHeaderSummaryAsync() =>
        (await page.Locator(".recruitment-dashboard-header p.text-muted").TextContentAsync())?.Trim() ?? "";

    /// <summary>Waits for the top KPI summary row (RecruitmentSummaryTile list) to finish loading.</summary>
    public async Task WaitForSummaryTilesLoadedAsync() =>
        await page.Locator(".widget-kpi-row[role='list']").WaitForAsync(new() { Timeout = 15_000 });

    /// <summary>Returns the numeric value of the top-row summary tile with the given label (e.g.
    /// "Open vacancies", "New applications", "Interviews requiring action", "Offers awaiting
    /// response", "Stale vacancies") — distinct from the Activity tab's RecruitmentSummaryWidget
    /// KPI row, which uses differently-cased/worded labels ("Open Vacancies", "Interviews
    /// Today", "Outstanding Feedback Tasks").</summary>
    public async Task<int> GetSummaryTileValueAsync(string label)
    {
        await WaitForSummaryTilesLoadedAsync();

        var tile = page.Locator(".widget-kpi-row[role='list'] .widget-kpi")
            .Filter(new() { HasText = label })
            .First;

        var text = (await tile.Locator(".widget-kpi-value").TextContentAsync())?.Trim() ?? "0";
        return int.Parse(text);
    }

    // ── Tabs (Pipeline / Activity / Insights) ────────────────────────────────

    public enum Tab { Pipeline, Activity, Insights }

    private ILocator TabButton(Tab tab) => page.Locator($"[data-testid='recruitment-tab-{tab.ToString().ToLowerInvariant()}']");

    public Task SwitchToTabAsync(Tab tab) => TabButton(tab).ClickAsync();

    public async Task<bool> IsTabActiveAsync(Tab tab) =>
        (await TabButton(tab).GetAttributeAsync("class"))?.Contains("active") ?? false;

    // ── Pipeline toolbar ──────────────────────────────────────────────────────

    private ILocator VacancyPickerWrapper => page.Locator(".recruitment-dashboard-vacancy-picker");

    /// <summary>
    /// Selects a vacancy in the Board view's vacancy picker. The picker's data-testid attribute
    /// lands directly on the SfDropDownList's own span[role='combobox'] (not a wrapping element),
    /// so DropDownSelector is scoped to the surrounding wrapper div instead (see
    /// VacancyKanbanBoardTests' identical note).
    /// </summary>
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
        // The checkbox's own label wraps a htmlFor click target too, but clicking the input
        // directly is unambiguous.
        await ShowClosedCandidatesCheckbox.ClickAsync();
        await page.WaitForTimeoutAsync(300);
    }

    public Task SwitchToBoardViewAsync() => page.Locator("[data-testid='recruitment-view-board-btn']").ClickAsync();

    public Task SwitchToListViewAsync() => page.Locator("[data-testid='recruitment-view-list-btn']").ClickAsync();

    // ── Header action buttons ────────────────────────────────────────────────

    public Task ClickCreateVacancyAsync() => page.Locator("[data-testid='recruitment-dashboard-create-vacancy-btn']").ClickAsync();

    public Task ClickAddCandidateAsync() => page.Locator("[data-testid='recruitment-dashboard-add-candidate-btn']").ClickAsync();

    /// <summary>Returns true if a widget with the given header title is present on the dashboard.
    /// Widgets live under the Activity tab (RecruitmentSummaryWidget, UpcomingInterviewsWidget,
    /// OffersAwaitingResponseWidget, OpenVacanciesNoActivityWidget) or the Insights tab
    /// (HiringPipelineChart, NewHiresTrendChart) since the redesign — this switches to that tab
    /// first if it isn't already active.</summary>
    public async Task<bool> HasWidgetAsync(string widgetTitle)
    {
        await EnsureTabForWidgetAsync(widgetTitle);
        return await page.Locator(".widget-header")
            .Filter(new() { HasText = widgetTitle })
            .IsVisibleAsync();
    }

    /// <summary>Waits for the named widget to finish loading (spinner replaced by items/empty state).</summary>
    public async Task WaitForWidgetLoadedAsync(string widgetTitle)
    {
        await EnsureTabForWidgetAsync(widgetTitle);
        var widget = page.Locator(".widget-card").Filter(new() { HasText = widgetTitle }).First;
        await widget.Locator(".task-widget-item, .widget-empty").First.WaitForAsync(new() { Timeout = 15_000 });
    }

    private static readonly string[] InsightsWidgetTitles = ["Hiring Pipeline", "New Hires"];

    /// <summary>Switches to the Insights tab for the chart widgets, or the Activity tab for
    /// everything else, unless the correct tab is already active.</summary>
    private async Task EnsureTabForWidgetAsync(string widgetTitle)
    {
        var targetTab = InsightsWidgetTitles.Any(t => widgetTitle.Contains(t, StringComparison.OrdinalIgnoreCase))
            ? Tab.Insights
            : Tab.Activity;

        if (!await IsTabActiveAsync(targetTab))
            await SwitchToTabAsync(targetTab);
    }

    // ── Hiring Pipeline / New Hires Trend charts ─────────────────────────────

    public async Task WaitForHiringPipelineChartLoadedAsync()
    {
        if (!await IsTabActiveAsync(Tab.Insights))
            await SwitchToTabAsync(Tab.Insights);

        var widget = page.Locator(".widget-card").Filter(new() { HasText = "Hiring Pipeline" });
        // SfAccumulationChart renders its funnel as an <svg>, not a ".e-accumulationchart-series"
        // element (that class doesn't exist in Syncfusion's actual DOM output) — "svg" is a
        // reliable signal the chart itself has rendered, regardless of its internal series markup.
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

    // ── Recruitment Summary Widget ────────────────────────────────────────────

    private ILocator RecruitmentWidget =>
        page.Locator(".widget-card").Filter(new() { HasText = "Recruitment" }).First;

    /// <summary>Waits for the Recruitment summary widget's KPI row to render (spinner gone).</summary>
    public async Task WaitForRecruitmentWidgetLoadedAsync()
    {
        if (!await IsTabActiveAsync(Tab.Activity))
            await SwitchToTabAsync(Tab.Activity);

        await RecruitmentWidget.Locator(".widget-kpi-row").WaitForAsync(new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Returns the numeric value shown under the given KPI label ("Open Vacancies",
    /// "Interviews Today", "Outstanding Feedback Tasks") in the Recruitment widget.
    /// </summary>
    public async Task<int> GetRecruitmentKpiValueAsync(string label)
    {
        await WaitForRecruitmentWidgetLoadedAsync();

        var kpi = RecruitmentWidget.Locator(".widget-kpi")
            .Filter(new() { HasText = label })
            .First;

        var text = (await kpi.Locator(".widget-kpi-value").TextContentAsync())?.Trim() ?? "0";
        return int.Parse(text);
    }

    /// <summary>Clicks the "Open Vacancies" KPI and waits for navigation to the vacancies list.</summary>
    public async Task ClickOpenVacanciesKpiAsync()
    {
        await WaitForRecruitmentWidgetLoadedAsync();

        await RecruitmentWidget.Locator(".widget-kpi")
            .Filter(new() { HasText = "Open Vacancies" })
            .First
            .ClickAsync();

        await page.WaitForURLAsync(new Regex("/vacancies"), new() { Timeout = 30_000 });
    }

    // ── Upcoming Interviews / Offers & Recent Hires / Stale Vacancies ────────

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

    // ── Metric tile drill-down dialog (DSH-04) ───────────────────────────────

    // RecruitmentMetricDrillDownDialog.razor renders an SfDialog with
    // CssClass="recruitment-metric-drilldown-dialog". Syncfusion copies that CssClass onto several
    // sibling nodes (outer wrapper, the dialog itself, the close button), so scope by the dialog
    // ROLE plus the class rather than the bare class alone — the [role='dialog'] qualifier keeps
    // this to the single actual dialog element.
    private ILocator DrillDownDialog => page.Locator("[role='dialog'].recruitment-metric-drilldown-dialog");

    private ILocator SummaryTile(string label) =>
        page.Locator(".widget-kpi-row[role='list'] .widget-kpi").Filter(new() { HasText = label }).First;

    /// <summary>Clicks the drillable summary tile with the given label and waits for its
    /// drill-down dialog to open.</summary>
    public async Task OpenMetricDrillDownAsync(string label)
    {
        await WaitForSummaryTilesLoadedAsync();
        await SummaryTile(label).ClickAsync();
        await DrillDownDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    /// <summary>
    /// Number of candidate rows shown in the currently-open drill-down dialog. The dialog renders a
    /// "Nothing to show." paragraph instead of a grid when the metric returned no items, which this
    /// reports as 0.
    /// </summary>
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

    /// <summary>True while a metric drill-down dialog is open.</summary>
    public Task<bool> IsMetricDrillDownOpenAsync() => DrillDownDialog.IsVisibleAsync();

    public async Task CloseMetricDrillDownAsync()
    {
        await DrillDownDialog.GetByRole(AriaRole.Button, new() { Name = "Close" }).First.ClickAsync();
        await DrillDownDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }
}
