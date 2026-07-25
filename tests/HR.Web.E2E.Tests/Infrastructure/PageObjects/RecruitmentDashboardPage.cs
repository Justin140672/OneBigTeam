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
        await page.WaitForSelectorAsync(".dashboard-greeting", new() { Timeout = 20_000 });
    }

    /// <summary>Returns true if a widget with the given header title is present on the dashboard.</summary>
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

    // ── Hiring Pipeline / New Hires Trend charts ─────────────────────────────

    public async Task WaitForHiringPipelineChartLoadedAsync()
    {
        var widget = page.Locator(".widget-card").Filter(new() { HasText = "Hiring Pipeline" });
        // SfAccumulationChart renders its funnel as an <svg>, not a ".e-accumulationchart-series"
        // element (that class doesn't exist in Syncfusion's actual DOM output) — "svg" is a
        // reliable signal the chart itself has rendered, regardless of its internal series markup.
        await widget.Locator("svg, .widget-empty").First
            .WaitForAsync(new() { Timeout = 15_000 });
    }

    public async Task WaitForNewHiresTrendChartLoadedAsync()
    {
        var widget = page.Locator(".widget-card").Filter(new() { HasText = "New Hires" });
        await widget.Locator(".e-chart, .widget-empty").First
            .WaitForAsync(new() { Timeout = 15_000 });
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

    // ── Vacancies / Candidates action buttons ────────────────────────────────

    public async Task ClickVacanciesButtonAsync()
    {
        await page.Locator("[data-testid='recruitment-dashboard-vacancies-btn']").ClickAsync();
        await page.WaitForURLAsync(new Regex("/vacancies"), new() { Timeout = 15_000 });
    }

    public async Task ClickCandidatesButtonAsync()
    {
        await page.Locator("[data-testid='recruitment-dashboard-candidates-btn']").ClickAsync();
        await page.WaitForURLAsync(new Regex("/candidates"), new() { Timeout = 15_000 });
    }

    // ── Upcoming Interviews / Offers & Recent Hires / Stale Vacancies ────────

    public async Task<IReadOnlyList<string>> GetUpcomingInterviewCandidateNamesAsync()
    {
        var widget = page.Locator(".widget-card").Filter(new() { HasText = "Upcoming Interviews" }).First;
        await widget.Locator(".task-widget-item, .widget-empty").First.WaitForAsync(new() { Timeout = 15_000 });

        var titles = await widget.Locator(".task-widget-title").AllAsync();
        var names  = new List<string>();
        foreach (var t in titles)
            names.Add((await t.TextContentAsync())?.Trim() ?? "");
        return names;
    }
}
