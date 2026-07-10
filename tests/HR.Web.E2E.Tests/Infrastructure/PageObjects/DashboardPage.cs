using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class DashboardPage(IPage page, string baseUrl)
{
    public async Task GoToAsync()
    {
        await page.GotoAsync($"{baseUrl}/");
        // Wait for the task widget to finish loading: either a task item appears or the
        // "All caught up!" empty state renders. Both are absent during the loading spinner phase.
        await page.WaitForSelectorAsync(".task-widget-item, .widget-empty", new() { Timeout = 20_000 });
    }

    /// <summary>
    /// Returns the titles of all task items currently shown in the My Tasks widget.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetTaskTitlesAsync()
    {
        var items = await page.Locator(".task-widget-item .task-widget-title").AllAsync();
        var titles = new List<string>();
        foreach (var item in items)
            titles.Add((await item.TextContentAsync())?.Trim() ?? "");
        return titles;
    }

    /// <summary>
    /// Clicks the first task item whose title contains <paramref name="titleFragment"/>.
    /// This opens TaskViewDialog in place (no navigation) — use TaskViewPage.WaitForLoadedAsync
    /// (or its own methods, which wait internally) to read the opened task's content.
    /// </summary>
    public async Task ClickTaskAsync(string titleFragment)
    {
        var item = page.Locator(".task-widget-item")
            .Filter(new() { HasText = titleFragment })
            .First;

        await item.ClickAsync();
        await page.WaitForSelectorAsync(".task-view-dialog", new() { Timeout = 15_000 });
    }

    /// <summary>Returns true if the My Tasks widget shows the "All caught up!" empty state.</summary>
    public async Task<bool> IsTaskListEmptyAsync() =>
        await page.Locator(".widget-empty").IsVisibleAsync();

    /// <summary>Clicks the "View all" link in the My Tasks widget and waits for navigation.</summary>
    public async Task ClickViewAllTasksAsync()
    {
        var myTasksWidget = page.Locator(".widget-card")
            .Filter(new() { HasText = "My Tasks" })
            .First;
        await myTasksWidget.Locator(".widget-view-all").ClickAsync();
        await page.WaitForURLAsync(new Regex("/profile"), new() { Timeout = 15_000 });
    }

    // ── Upcoming Probation Reviews Widget ─────────────────────────────────────

    /// <summary>Returns true if the Upcoming Probation Reviews widget header is visible.</summary>
    public async Task<bool> HasUpcomingProbationWidgetAsync() =>
        await page.Locator(".widget-header")
            .Filter(new() { HasText = "Upcoming Probation Reviews" })
            .IsVisibleAsync();

    /// <summary>
    /// Returns the employee names shown in the Upcoming Probation Reviews widget items.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetUpcomingProbationEmployeeNamesAsync()
    {
        var probationWidget = page.Locator(".widget-card")
            .Filter(new() { HasText = "Upcoming Probation Reviews" });

        // Wait for the widget to finish loading — items or empty state replaces the spinner.
        await probationWidget
            .Locator(".task-widget-item, .widget-empty")
            .First
            .WaitForAsync(new() { Timeout = 15_000 });

        var titles = await probationWidget.Locator(".task-widget-title").AllAsync();
        var names  = new List<string>();
        foreach (var t in titles)
            names.Add((await t.TextContentAsync())?.Trim() ?? "");
        return names;
    }

    // ── My Assets Widget ──────────────────────────────────────────────────────

    private ILocator MyAssetsWidget =>
        page.Locator(".widget-card").Filter(new() { HasText = "My Assets" }).First;

    /// <summary>Waits for the My Assets widget to finish loading (spinner gone).</summary>
    private async Task WaitForMyAssetsWidgetAsync()
    {
        // Wait until either an asset item or the empty state is present.
        await MyAssetsWidget
            .Locator(".task-widget-item, .widget-empty")
            .First
            .WaitForAsync(new() { Timeout = 15_000 });
    }

    /// <summary>Returns true if the My Assets widget header is visible on the dashboard.</summary>
    public async Task<bool> HasMyAssetsWidgetAsync() =>
        await page.Locator(".widget-header")
            .Filter(new() { HasText = "My Assets" })
            .IsVisibleAsync();

    /// <summary>Returns the asset names listed in the My Assets widget.</summary>
    public async Task<IReadOnlyList<string>> GetMyAssetNamesAsync()
    {
        await WaitForMyAssetsWidgetAsync();

        var titles = await MyAssetsWidget.Locator(".task-widget-title").AllAsync();
        var names  = new List<string>();
        foreach (var t in titles)
            names.Add((await t.TextContentAsync())?.Trim() ?? "");
        return names;
    }

    /// <summary>Returns true if the My Assets widget shows the "No assets assigned." empty state.</summary>
    public async Task<bool> IsMyAssetsWidgetEmptyAsync()
    {
        await WaitForMyAssetsWidgetAsync();
        return await MyAssetsWidget.Locator(".widget-empty").IsVisibleAsync();
    }

    /// <summary>
    /// Returns the acknowledgement badge text ("Pending" or "Acknowledged") for the
    /// asset whose name contains <paramref name="assetNameFragment"/>.
    /// </summary>
    public async Task<string> GetMyAssetAcknowledgementBadgeAsync(string assetNameFragment)
    {
        await WaitForMyAssetsWidgetAsync();

        var item = MyAssetsWidget.Locator(".task-widget-item")
            .Filter(new() { HasText = assetNameFragment })
            .First;

        var badge = item.Locator(".due-badge");
        return (await badge.TextContentAsync())?.Trim() ?? "";
    }

    /// <summary>
    /// Clicks the asset item whose name contains <paramref name="assetNameFragment"/>
    /// and waits for navigation to the asset detail page.
    /// </summary>
    public async Task ClickMyAssetAsync(string assetNameFragment)
    {
        await WaitForMyAssetsWidgetAsync();

        var item = MyAssetsWidget.Locator(".task-widget-item")
            .Filter(new() { HasText = assetNameFragment })
            .First;

        await item.ClickAsync();
        await page.WaitForURLAsync(new Regex("/assets/"), new() { Timeout = 15_000 });
    }

    // ── Recruitment Summary Widget ────────────────────────────────────────────

    private ILocator RecruitmentWidget =>
        page.Locator(".widget-card").Filter(new() { HasText = "Recruitment" }).First;

    /// <summary>Waits for the Recruitment summary widget's KPI row to render (spinner gone).</summary>
    public async Task WaitForRecruitmentWidgetLoadedAsync() =>
        await RecruitmentWidget.Locator(".widget-kpi-row").WaitForAsync(new() { Timeout = 15_000 });

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

        await page.WaitForURLAsync(new Regex("/vacancies"), new() { Timeout = 15_000 });
    }

    // ── Sickness Dashboard Widgets ────────────────────────────────────────────

    /// <summary>
    /// Returns true if a widget with the given header title is present on the dashboard.
    /// Used to verify HR-only sickness widgets (Current Sickness Absence, Overdue
    /// Return-to-Work Reviews, Missing Fit Notes) are shown/hidden based on permissions.
    /// </summary>
    public async Task<bool> HasWidgetAsync(string widgetTitle) =>
        await page.Locator(".widget-header")
            .Filter(new() { HasText = widgetTitle })
            .IsVisibleAsync();

    /// <summary>Waits for the named widget to finish loading (spinner replaced by items/empty state).</summary>
    public async Task WaitForWidgetLoadedAsync(string widgetTitle)
    {
        var widget = page.Locator(".widget-card").Filter(new() { HasText = widgetTitle }).First;
        await widget.Locator(".task-widget-item, .widget-empty").First.WaitForAsync(new() { Timeout = 15_000 });
    }
}
