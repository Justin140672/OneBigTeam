using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Interacts with the Manager-only dashboard
/// (src/HR.Web/Components/Pages/Dashboards/ManagerDashboard.razor), reached by navigating
/// directly to "/dashboard/manager". The page guards on Session.IsManager and redirects any
/// other role to Session.MyProfileUrl before the widgets below ever render.
///
/// Note: not every widget on this dashboard is gated the same way as the route itself. Most gate
/// on Session.IsManager (matching the route guard), but TeamOnboardingWidget additionally
/// requires Session.CanManageEmployees (the HrAdministrator-only "employee:manage" permission) —
/// so a Manager-only persona (e.g. James Okafor) will not see Team Onboarding even though they
/// can reach this dashboard, while a Manager who is also an HrAdministrator (e.g. David Park)
/// will. See HR.Web.Components.Pages.Onboarding.TeamOnboardingWidget.razor.
/// </summary>
public sealed class ManagerDashboardPage(IPage page, string baseUrl)
{
    public async Task GoToAsync()
    {
        await page.GotoAsync($"{baseUrl}/dashboard/manager");
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

    // ── Team Tasks Widget ──────────────────────────────────────────────────────

    private ILocator TeamTasksWidget =>
        page.Locator(".widget-card").Filter(new() { HasText = "Team Tasks" }).First;

    public async Task<IReadOnlyList<string>> GetTeamTaskTitlesAsync()
    {
        await TeamTasksWidget.Locator(".task-widget-item, .widget-empty").First.WaitForAsync(new() { Timeout = 15_000 });

        var titles = await TeamTasksWidget.Locator(".task-widget-title").AllAsync();
        var names  = new List<string>();
        foreach (var t in titles)
            names.Add((await t.TextContentAsync())?.Trim() ?? "");
        return names;
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
    public async Task ClickLeaveRequestItemAsync(string nameFragment)
    {
        await LeaveRequestsWidget.Locator(".task-widget-item, .widget-empty").First
            .WaitForAsync(new() { Timeout = 15_000 });

        await LeaveRequestsWidget.Locator(".task-widget-item")
            .Filter(new() { HasText = nameFragment })
            .First
            .ClickAsync();
    }

    // ── My Team Widget ────────────────────────────────────────────────────────

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

    /// <summary>
    /// Returns the visible phone/email contact text (MyTeamWidget.razor's
    /// ".team-card-contact-text" spans) for the team-member card whose name contains
    /// <paramref name="nameFragment"/> — proves the phone number/email are rendered as visible
    /// text next to their icons, not just present in a hidden "title" tooltip attribute.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetTeamMemberContactTextAsync(string nameFragment)
    {
        var card = MyTeamWidget.Locator(".team-card").Filter(new() { HasText = nameFragment }).First;
        var spans = await card.Locator(".team-card-contact-text").AllAsync();

        var values = new List<string>();
        foreach (var s in spans)
            values.Add((await s.TextContentAsync())?.Trim() ?? "");
        return values;
    }

    /// <summary>
    /// Returns the status badge text (".team-card-status") for the team-member card whose name
    /// contains <paramref name="nameFragment"/> — "At Work", "Sick", or "On Leave"
    /// (MyTeamWidget.razor's StatusLabel).
    /// </summary>
    public async Task<string> GetTeamMemberStatusAsync(string nameFragment)
    {
        var card = MyTeamWidget.Locator(".team-card").Filter(new() { HasText = nameFragment }).First;
        return (await card.Locator(".team-card-status").TextContentAsync())?.Trim() ?? "";
    }

    /// <summary>
    /// Clicks "Notify Sickness" on the team-member card whose name contains
    /// <paramref name="nameFragment"/>, opening RecordSicknessDialog for that employee
    /// (MyTeamWidget.razor's OpenNotifySickness).
    /// </summary>
    public async Task ClickNotifySicknessForTeamMemberAsync(string nameFragment)
    {
        var card = MyTeamWidget.Locator(".team-card").Filter(new() { HasText = nameFragment }).First;
        await card.GetByRole(AriaRole.Button, new() { Name = "Notify Sickness" }).ClickAsync();
        await page.WaitForSelectorAsync("[role='dialog'].record-sickness-dialog", new() { Timeout = 10_000 });
    }

    // ── Upcoming Probation Reviews Widget ─────────────────────────────────────

    private ILocator UpcomingProbationWidget =>
        page.Locator(".widget-card").Filter(new() { HasText = "Upcoming Probation Reviews" }).First;

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

    // ── Team Onboarding Widget ─────────────────────────────────────────────────

    private ILocator TeamOnboardingWidget =>
        page.Locator(".widget-card").Filter(new() { HasText = "Team Onboarding" }).First;

    /// <summary>
    /// Returns the employee names shown in the Team Onboarding widget items. Only call this for
    /// a persona with Session.CanManageEmployees (see class remarks) — for a Manager-only
    /// persona the widget never renders at all, so this would time out.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetTeamOnboardingEmployeeNamesAsync()
    {
        await TeamOnboardingWidget.Locator(".task-widget-item, .widget-empty").First
            .WaitForAsync(new() { Timeout = 15_000 });

        var titles = await TeamOnboardingWidget.Locator(".task-widget-title").AllAsync();
        var names  = new List<string>();
        foreach (var t in titles)
            names.Add((await t.TextContentAsync())?.Trim() ?? "");
        return names;
    }

    /// <summary>
    /// Clicks the first Team Onboarding item whose title contains <paramref name="nameFragment"/>
    /// and waits for navigation to the employee's profile with the Onboarding tab active.
    /// </summary>
    public async Task ClickTeamOnboardingItemAsync(string nameFragment)
    {
        await TeamOnboardingWidget.Locator(".task-widget-item, .widget-empty").First
            .WaitForAsync(new() { Timeout = 15_000 });

        await TeamOnboardingWidget.Locator(".task-widget-item")
            .Filter(new() { HasText = nameFragment })
            .First
            .ClickAsync();

        await page.WaitForURLAsync(new Regex(@"/employees/[0-9a-f-]{36}\?tab=onboarding"), new() { Timeout = 15_000 });
    }

    // ── Team Sickness Today Widget ────────────────────────────────────────────

    private ILocator TeamSicknessTodayWidget =>
        page.Locator(".widget-card").Filter(new() { HasText = "Team Sickness Today" }).First;

    public async Task<bool> IsTeamSicknessTodayEmptyAsync()
    {
        await TeamSicknessTodayWidget.Locator(".task-widget-item, .widget-empty").First
            .WaitForAsync(new() { Timeout = 15_000 });
        return await TeamSicknessTodayWidget.Locator(".widget-empty").IsVisibleAsync();
    }
}
