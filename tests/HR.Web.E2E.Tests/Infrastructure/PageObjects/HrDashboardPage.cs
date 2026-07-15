using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Interacts with the HR-only dashboard
/// (src/HR.Web/Components/Pages/Dashboards/HrDashboard.razor), reached by navigating directly to
/// "/dashboard/hr". The page guards on Session.IsHrAdministrator and redirects any other role to
/// Session.MyProfileUrl before the widgets below ever render — GoToAsync does not wait for that
/// redirect itself, so a caller exercising the denial path should assert on the resulting page
/// (e.g. via page.WaitForURLAsync) rather than calling the widget getters below.
/// </summary>
public sealed class HrDashboardPage(IPage page, string baseUrl)
{
    public async Task GoToAsync()
    {
        await page.GotoAsync($"{baseUrl}/dashboard/hr");
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

    // ── Headcount by Department Chart ────────────────────────────────────────

    /// <summary>Waits for the Headcount by Department chart tile to finish loading.</summary>
    public async Task WaitForHeadcountChartLoadedAsync()
    {
        var widget = page.Locator(".widget-card").Filter(new() { HasText = "Headcount by Department" });
        await widget.Locator(".e-accumulationchart-series, .widget-empty").First
            .WaitForAsync(new() { Timeout = 15_000 });
    }

    // ── HR Inbox Widget ───────────────────────────────────────────────────────

    private ILocator HrInboxWidget => page.Locator(".widget-card").Filter(new() { HasText = "HR Inbox" }).First;

    public async Task<IReadOnlyList<string>> GetHrInboxTaskTitlesAsync()
    {
        await HrInboxWidget.Locator(".task-widget-item, .widget-empty").First.WaitForAsync(new() { Timeout = 15_000 });

        var titles = await HrInboxWidget.Locator(".task-widget-title").AllAsync();
        var names  = new List<string>();
        foreach (var t in titles)
            names.Add((await t.TextContentAsync())?.Trim() ?? "");
        return names;
    }

    /// <summary>Clicks the "View all" link in the HR Inbox widget and waits for navigation.</summary>
    public async Task ClickHrInboxViewAllAsync()
    {
        await HrInboxWidget.Locator(".widget-view-all").ClickAsync();
        await page.WaitForURLAsync(new Regex("/hr/inbox"), new() { Timeout = 15_000 });
    }

    // ── Leave Requests Widget ─────────────────────────────────────────────────

    private ILocator LeaveRequestsWidget =>
        page.Locator(".widget-card").Filter(new() { HasText = "Leave Requests" }).First;

    public async Task<IReadOnlyList<string>> GetLeaveRequestEmployeeNamesAsync()
    {
        await LeaveRequestsWidget.Locator(".task-widget-item, .widget-empty").First
            .WaitForAsync(new() { Timeout = 15_000 });

        var titles = await LeaveRequestsWidget.Locator(".task-widget-title").AllAsync();
        var names  = new List<string>();
        foreach (var t in titles)
            names.Add((await t.TextContentAsync())?.Trim() ?? "");
        return names;
    }

    /// <summary>
    /// Clicks the Leave Requests widget row whose employee name contains
    /// <paramref name="nameFragment"/> (LeaveRequestsWidget.razor's NavigateToRequest). If the
    /// request still has an open leave-approval task, this opens TaskViewDialog in place (use
    /// TaskViewPage to interact with it); otherwise it navigates away to that employee's profile
    /// Leave tab. Callers should assert on whichever outcome they expect.
    /// </summary>
    public async Task ClickLeaveRequestItemAsync(string nameFragment) =>
        await LeaveRequestsWidget.Locator(".task-widget-item")
            .Filter(new() { HasText = nameFragment })
            .First
            .ClickAsync();

    // ── Upcoming Probation Reviews Widget ─────────────────────────────────────

    private ILocator UpcomingProbationWidget =>
        page.Locator(".widget-card").Filter(new() { HasText = "Upcoming Probation Reviews" }).First;

    /// <summary>Returns the employee names shown in the Upcoming Probation Reviews widget items.</summary>
    public async Task<IReadOnlyList<string>> GetUpcomingProbationEmployeeNamesAsync()
    {
        await UpcomingProbationWidget.Locator(".task-widget-item, .widget-empty").First
            .WaitForAsync(new() { Timeout = 15_000 });

        var titles = await UpcomingProbationWidget.Locator(".task-widget-title").AllAsync();
        var names  = new List<string>();
        foreach (var t in titles)
            names.Add((await t.TextContentAsync())?.Trim() ?? "");
        return names;
    }

    /// <summary>Clicks the first item in the Upcoming Probation Reviews widget and waits for navigation.</summary>
    public async Task ClickFirstUpcomingProbationReviewAsync()
    {
        await UpcomingProbationWidget.Locator(".task-widget-item").First.ClickAsync();
        await page.WaitForURLAsync(new Regex(@"/employees/[0-9a-f-]{36}\?tab=probation"), new() { Timeout = 15_000 });
    }
}
