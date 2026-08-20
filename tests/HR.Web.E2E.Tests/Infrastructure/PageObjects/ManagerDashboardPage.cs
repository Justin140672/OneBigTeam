using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Interacts with the Manager-only dashboard
/// (src/HR.Web/Components/Pages/Dashboards/ManagerDashboard.razor), reached by navigating
/// directly to "/dashboard/manager". The page guards on Session.IsManager and redirects any
/// other role to Session.MyProfileUrl before the widgets below ever render.
///
/// Redesigned dashboard layout (see PRODUCT ticket "Reorganise the Team Manager Dashboard around
/// priority actions"): the former standalone Team Tasks, Leave Requests, Upcoming Probation
/// Reviews, Overdue Return-to-Work Reviews and Missing Fit Notes widgets were folded into a
/// single combined "Requires your attention" queue (ManagerAttentionQueueWidget.razor). A new
/// compact "Team Status" metric strip (TeamStatusSummary.razor) was added, and the
/// TeamOnboardingWidget / TeamSicknessTodayWidget cards were removed from this page entirely
/// (they remain used elsewhere — TeamOnboardingWidget is unused now, MissingFitNotesWidget is
/// still used standalone on the HR dashboard). My Team (MyTeamWidget) and Reports
/// (TeamReportsWidget) are unchanged structurally.
/// </summary>
public sealed class ManagerDashboardPage(IPage page, string baseUrl)
{
    public async Task GoToAsync()
    {
        await page.GotoAsync($"{baseUrl}/dashboard/manager");
        // Bumped 20s -> 35s: several tests in this file call this right after
        // CreateEmployeeReportingToDavidAsync's full-form UI employee creation, and under the
        // higher concurrent load from the many tests that now create fresh employees the same
        // way, the dashboard's own widget data load can genuinely take longer than 20s. Same
        // load-timing theory as the employee-save navigation timeout fix.
        await page.WaitForSelectorAsync(".dashboard-greeting", new() { Timeout = 35_000 });
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
        await widget.Locator(".task-widget-item, .widget-empty, .attention-queue-all-clear").First.WaitForAsync(new() { Timeout = 15_000 });
    }

    // ── "Requires your attention" combined queue (ManagerAttentionQueueWidget.razor) ─────────
    //
    // Replaces the old per-category widgets (Team Tasks, Leave Requests, Upcoming Probation
    // Reviews, Overdue Return-to-Work Reviews, Missing Fit Notes). Each row is a single button
    // element (class "task-widget-item attention-queue-item", plus "attention-queue-item--overdue"
    // when overdue) whose text content includes both the row's subject (".task-widget-title") and
    // its category/status (".task-widget-meta", e.g. "Leave request · Pending"), so filtering by
    // either the subject or the category text both work via Playwright's HasText. This mirrors
    // HrDashboardPage's equivalent accessors for AttentionQueueWidget.razor.

    private ILocator AttentionQueueWidget =>
        page.Locator(".widget-card.attention-queue-card").First;

    /// <summary>Waits for the attention queue to finish loading (spinner replaced by rows/empty state).</summary>
    public async Task WaitForAttentionQueueLoadedAsync() =>
        await AttentionQueueWidget.Locator(".attention-queue-item, .attention-queue-all-clear").First
            .WaitForAsync(new() { Timeout = 15_000 });

    /// <summary>
    /// Returns the subject (".task-widget-title") of every row currently in the attention queue.
    /// Pass <paramref name="categoryFilter"/> (e.g. "Team task", "Leave request", "Probation
    /// review", "Return-to-work review", "Fit note evidence") to scope to one category — the
    /// filter matches against the whole row's text, which includes both title and category/meta.
    /// </summary>
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

    /// <summary>Returns true if the attention-queue row matching <paramref name="subjectFragment"/> is styled overdue.</summary>
    public async Task<bool> IsAttentionQueueItemOverdueAsync(string subjectFragment)
    {
        await WaitForAttentionQueueLoadedAsync();
        var row = AttentionQueueWidget.Locator(".attention-queue-item").Filter(new() { HasText = subjectFragment }).First;
        var classes = await row.GetAttributeAsync("class") ?? "";
        return classes.Contains("attention-queue-item--overdue");
    }

    /// <summary>Returns true if the attention queue is showing its "All clear" empty state.</summary>
    public async Task<bool> AttentionQueueIsAllClearAsync() =>
        await AttentionQueueWidget.Locator(".attention-queue-all-clear").IsVisibleAsync();

    /// <summary>
    /// Clicks the first attention-queue row whose text (subject or category/meta) contains
    /// <paramref name="textFragment"/>. If the row's underlying item has an open task, this opens
    /// TaskViewDialog in place (use TaskViewPage to interact with it); otherwise it navigates away
    /// (e.g. to an employee's profile). Callers should assert on whichever outcome they expect.
    /// </summary>
    public async Task ClickAttentionQueueItemAsync(string textFragment)
    {
        await WaitForAttentionQueueLoadedAsync();
        await AttentionQueueWidget.Locator(".attention-queue-item")
            .Filter(new() { HasText = textFragment })
            .First
            .ClickAsync();
    }

    // ── Team Status Summary (TeamStatusSummary.razor) ─────────────────────────────────────────
    //
    // Compact metric strip added by the redesign. Tiles are not clickable/filterable (see the
    // component's own remarks), so this only exposes read access to the displayed counts.

    private ILocator TeamStatusWidget =>
        page.Locator(".widget-card.team-status-summary").First;

    public async Task WaitForTeamStatusLoadedAsync() =>
        await TeamStatusWidget.Locator(".team-status-tile, .widget-empty").First
            .WaitForAsync(new() { Timeout = 15_000 });

    /// <summary>
    /// Returns the numeric value shown on the Team Status tile whose label (".team-status-label")
    /// exactly matches <paramref name="tileLabel"/> (e.g. "At work", "Away today", "On leave",
    /// "Sick", "In probation", "Missing fit notes").
    /// </summary>
    public async Task<int> GetTeamStatusValueAsync(string tileLabel)
    {
        await WaitForTeamStatusLoadedAsync();
        var tile = TeamStatusWidget.Locator(".team-status-tile")
            .Filter(new() { Has = page.Locator(".team-status-label", new() { HasText = tileLabel }) })
            .First;
        var text = await tile.Locator(".team-status-value").TextContentAsync();
        return int.TryParse(text?.Trim(), out var value) ? value : 0;
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

        // GetMyTeamMemberNamesAsync only waits for ".team-card"/".widget-empty" to exist, which
        // proves the cards themselves have rendered but not that each card's own contact-text
        // spans (a separate nested render) have populated yet — reading immediately after can
        // observe 0, or occasionally a previous card's stale content mid-swap. Wait for at least
        // one contact-text span on this specific card before reading.
        await card.Locator(".team-card-contact-text").First.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

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
}
