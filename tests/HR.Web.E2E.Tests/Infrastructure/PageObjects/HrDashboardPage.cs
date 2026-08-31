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
    // Converted from a Syncfusion donut chart to clickable horizontal-bar rows
    // (HeadcountByDepartmentChart.razor, class "hbar-chart-interactive" / "hbar-row hbar-row--button").
    // Each row is a real <button> with an aria-label ("View {n} employees in {dept}") and opens
    // EmployeesByDepartmentDialog on click, same as before.

    private ILocator HeadcountWidget =>
        page.Locator(".widget-card").Filter(new() { HasText = "Headcount by Department" }).First;

    /// <summary>Waits for the Headcount by Department chart tile to finish loading (bar rows or empty state).</summary>
    public async Task WaitForHeadcountChartLoadedAsync() =>
        await HeadcountWidget.Locator(".hbar-row--button, .widget-empty").First
            .WaitForAsync(new() { Timeout = 15_000 });

    /// <summary>Returns the department labels shown as plain text next to each bar, in DOM order.</summary>
    public async Task<IReadOnlyList<string>> GetHeadcountDepartmentLabelsAsync()
    {
        await WaitForHeadcountChartLoadedAsync();
        var labels = await HeadcountWidget.Locator(".hbar-row--button .hbar-label").AllAsync();
        var names  = new List<string>();
        foreach (var l in labels)
            names.Add((await l.TextContentAsync())?.Trim() ?? "");
        return names;
    }

    /// <summary>Clicks the "View all employees" link and waits for navigation to the employees list.</summary>
    public async Task ClickHeadcountViewAllEmployeesAsync()
    {
        await HeadcountWidget.GetByRole(AriaRole.Link, new() { Name = "View all employees" }).ClickAsync();
        await page.WaitForURLAsync(new Regex(@"/companies/[0-9a-f-]{36}/employees$"), new() { Timeout = 15_000 });
    }

    // ── Gender Split Chart ───────────────────────────────────────────────────
    // GenderSplitChart.razor ("Gender Split") — converted from a Syncfusion donut chart to the
    // shared HorizontalBarChart control (Components/Controls/HorizontalBarChart.razor). Renders
    // ".hbar-chart" with plain-text ".hbar-label" spans (no hover/tooltip required to read a
    // category), or ".widget-empty" with "No employee data available." when there are no active
    // employees.

    private ILocator GenderSplitWidget =>
        page.Locator(".widget-card").Filter(new() { HasText = "Gender Split" }).First;

    /// <summary>Waits for the Gender Split chart tile to finish loading (bar chart or empty state).</summary>
    public async Task WaitForGenderSplitChartLoadedAsync() =>
        await GenderSplitWidget.Locator(".hbar-chart, .widget-empty").First
            .WaitForAsync(new() { Timeout = 15_000 });

    /// <summary>Returns true once loaded if the Gender Split widget is showing its empty state.</summary>
    public async Task<bool> GenderSplitChartIsEmptyAsync() =>
        await GenderSplitWidget.Locator(".widget-empty").IsVisibleAsync();

    /// <summary>Returns the visible category labels rendered as plain text (not requiring hover) in the bar chart.</summary>
    public async Task<IReadOnlyList<string>> GetGenderSplitLabelsAsync()
    {
        await WaitForGenderSplitChartLoadedAsync();
        var labels = await GenderSplitWidget.Locator(".hbar-label").AllAsync();
        var names  = new List<string>();
        foreach (var l in labels)
            names.Add((await l.TextContentAsync())?.Trim() ?? "");
        return names;
    }

    // ── Employment Type Split Chart ──────────────────────────────────────────
    // EmploymentTypeSplitChart.razor ("Employment Type") — identical structure/behavior
    // to GenderSplitChart above (shared HorizontalBarChart control), grouped by employment
    // type instead of gender.

    private ILocator EmploymentTypeSplitWidget =>
        page.Locator(".widget-card").Filter(new() { HasText = "Employment Type" }).First;

    /// <summary>Waits for the Employment Type chart tile to finish loading (bar chart or empty state).</summary>
    public async Task WaitForEmploymentTypeSplitChartLoadedAsync() =>
        await EmploymentTypeSplitWidget.Locator(".hbar-chart, .widget-empty").First
            .WaitForAsync(new() { Timeout = 15_000 });

    /// <summary>Returns true once loaded if the Employment Type widget is showing its empty state.</summary>
    public async Task<bool> EmploymentTypeSplitChartIsEmptyAsync() =>
        await EmploymentTypeSplitWidget.Locator(".widget-empty").IsVisibleAsync();

    /// <summary>Returns the visible category labels rendered as plain text (not requiring hover) in the bar chart.</summary>
    public async Task<IReadOnlyList<string>> GetEmploymentTypeSplitLabelsAsync()
    {
        await WaitForEmploymentTypeSplitChartLoadedAsync();
        var labels = await EmploymentTypeSplitWidget.Locator(".hbar-label").AllAsync();
        var names  = new List<string>();
        foreach (var l in labels)
            names.Add((await l.TextContentAsync())?.Trim() ?? "");
        return names;
    }

    /// <summary>Returns the bounding boxes of the three analytics chart tiles (Headcount, Gender, Employment Type), in DOM order.</summary>
    public async Task<IReadOnlyList<LayoutRect>> GetAnalyticsGridTileBoundsAsync()
    {
        var tiles = page.Locator(".dashboard-analytics-grid > .widget-card, .dashboard-analytics-grid > .chart-tile");
        var count = await tiles.CountAsync();
        var bounds = new List<LayoutRect>();
        for (var i = 0; i < count; i++)
        {
            var box = await tiles.Nth(i).BoundingBoxAsync();
            if (box is not null)
                bounds.Add(new LayoutRect(box.X, box.Y, box.Width, box.Height));
        }
        return bounds;
    }

    public readonly record struct LayoutRect(float X, float Y, float Width, float Height);

    // ── "Needs your attention" unified queue (AttentionQueueWidget.razor) ────────
    // Replaced the standalone HrInboxWidget / LeaveRequestsWidget / UpcomingProbationReviewsWidget
    // / OverdueReturnToWorkReviewsWidget / ComplianceDocumentExpiryWidget / DocumentReviewsWidget
    // widgets on this page with a single merged, priority-sorted queue. Rows are <button>
    // elements (class "task-widget-item attention-queue-item", plus "attention-queue-item--overdue"
    // when overdue) with a descriptive aria-label built from Subject/Category/StatusLabel/DueLabel/
    // ActionLabel (see AttentionItem.AccessibleLabel) rather than a generic "View all"-style name.

    private ILocator AttentionQueueWidget =>
        page.Locator(".widget-card.attention-queue-card").First;

    /// <summary>Waits for the attention queue to finish loading (items rendered or "All clear" shown).</summary>
    public async Task WaitForAttentionQueueLoadedAsync() =>
        await AttentionQueueWidget.Locator(".attention-queue-item, .attention-queue-all-clear").First
            .WaitForAsync(new() { Timeout = 15_000 });

    /// <summary>Returns the subject text (task-widget-title) of every currently visible queue row, in DOM order.</summary>
    public async Task<IReadOnlyList<string>> GetAttentionQueueSubjectsAsync()
    {
        await WaitForAttentionQueueLoadedAsync();
        var titles = await AttentionQueueWidget.Locator(".attention-queue-item .task-widget-title").AllAsync();
        var names  = new List<string>();
        foreach (var t in titles)
            names.Add((await t.TextContentAsync())?.Trim() ?? "");
        return names;
    }

    /// <summary>Returns true once loaded if any queue row for the given subject fragment is marked overdue.</summary>
    public async Task<bool> IsAttentionQueueItemOverdueAsync(string subjectFragment)
    {
        await WaitForAttentionQueueLoadedAsync();
        var row = AttentionQueueWidget.Locator(".attention-queue-item").Filter(new() { HasText = subjectFragment }).First;
        var classes = await row.GetAttributeAsync("class") ?? "";
        return classes.Contains("attention-queue-item--overdue");
    }

    /// <summary>Returns true if the "All clear" compact empty-state summary is showing instead of any queue rows.</summary>
    public async Task<bool> AttentionQueueIsAllClearAsync() =>
        await AttentionQueueWidget.Locator(".attention-queue-all-clear").IsVisibleAsync();

    /// <summary>
    /// Clicks the first queue row whose accessible name/text contains <paramref name="subjectFragment"/>.
    /// Task-backed rows (HR tasks, leave requests with an open review task, probation/return-to-work
    /// reviews with a generated task) open TaskViewDialog in place; other rows navigate away to the
    /// relevant employee/document page. Callers should assert on whichever outcome they expect.
    /// </summary>
    public async Task ClickAttentionQueueItemAsync(string subjectFragment)
    {
        await WaitForAttentionQueueLoadedAsync();
        await AttentionQueueWidget.Locator(".attention-queue-item")
            .Filter(new() { HasText = subjectFragment })
            .First
            .ClickAsync();
    }

    // ── DSH-06: single bounded summary fetch (GET .../dashboards/hr/summary) ──────
    // The "Show resolved leave requests" toggle and per-source individual retry buttons were
    // removed — the widget now issues one server-side summary request, maps each returned
    // category to a WidgetSourceOutcome, and shows a single retry-all control (either the
    // whole-widget "Your action queue" WidgetSourceWarning when the fetch itself fails, or one
    // per partially-failed category — all wired to the same ReloadAllAsync).

    /// <summary>The number shown in the widget's count badge (".widget-count-badge"), or 0 if not rendered.</summary>
    public async Task<int> GetAttentionQueueCountBadgeAsync()
    {
        var badge = AttentionQueueWidget.Locator(".widget-count-badge").First;
        if (!await badge.IsVisibleAsync())
            return 0;
        var text = (await badge.TextContentAsync())?.Trim();
        return int.TryParse(text, out var value) ? value : 0;
    }

    /// <summary>Count of currently rendered attention-queue rows.</summary>
    public async Task<int> GetAttentionQueueRowCountAsync()
    {
        await WaitForAttentionQueueLoadedAsync();
        return await AttentionQueueWidget.Locator(".attention-queue-item").CountAsync();
    }

    /// <summary>Number of inline per-source failure warnings (".widget-source-warning") shown inside the card.</summary>
    public async Task<int> GetAttentionQueueSourceWarningCountAsync() =>
        await AttentionQueueWidget.Locator(".widget-source-warning").CountAsync();

    /// <summary>True if an inline warning whose text contains <paramref name="sourceName"/> is visible in the card.</summary>
    public async Task<bool> HasAttentionQueueSourceWarningAsync(string sourceName) =>
        await AttentionQueueWidget.Locator(".widget-source-warning")
            .Filter(new() { HasText = sourceName }).First.IsVisibleAsync();

    /// <summary>Clicks the retry-all control on the first inline source warning in the card.</summary>
    public async Task RetryAttentionQueueAllAsync() =>
        await AttentionQueueWidget.Locator(".widget-source-warning .widget-source-warning-retry")
            .First.ClickAsync();

    /// <summary>Waits until no inline source warning remains in the card (successful retry-all).</summary>
    public async Task WaitForAttentionQueueSourceWarningsClearedAsync() =>
        await AttentionQueueWidget.Locator(".widget-source-warning").First
            .WaitForAsync(new() { State = WaitForSelectorState.Detached, Timeout = 20_000 });

    // ── Document review rows within the attention queue ──────────────────────
    // Company document reviews (formerly the standalone DocumentReviewsWidget) now render as
    // "Document review" category rows inside AttentionQueueWidget — subject is the document
    // title, action label is "Review document", and clicking navigates straight to the
    // SharedCompanyDocument detail route.

    /// <summary>
    /// Clicks the attention-queue row whose subject contains <paramref name="titleFragment"/>
    /// (a "Document review" category row) and waits for navigation to
    /// "/companies/{companyId}/shared-documents/{documentId}".
    /// </summary>
    public async Task ClickDocumentReviewItemAsync(string titleFragment)
    {
        await ClickAttentionQueueItemAsync(titleFragment);
        await page.WaitForURLAsync(
            new Regex(@"/companies/[0-9a-f-]{36}/shared-documents/[0-9a-f-]{36}"),
            new() { Timeout = 15_000 });
    }

    // ── Favourite Reports Widget ──────────────────────────────────────────────
    // FavouriteReportsWidget.razor ("Favourite Reports") — mirrors ReportCatalogPage.razor's own
    // favourites (server-persisted via ReportingService.GetReportFavouritesAsync/Add/Remove), just
    // read-only here: this widget only lists whatever is already favourited from the Reports page,
    // it has no star toggle of its own.

    private ILocator FavouriteReportsWidget =>
        page.Locator(".widget-card").Filter(new() { HasText = "Favourite Reports" }).First;

    /// <summary>Returns the report titles shown in the Favourite Reports widget items.</summary>
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

    /// <summary>
    /// Clicks the Favourite Reports widget row whose title contains <paramref name="titleFragment"/>
    /// (FavouriteReportsWidget.razor's OpenReport navigates straight to the report's route via
    /// ReportRoutes.RouteFor) and waits for navigation to "/companies/{companyId}/reporting/{route}".
    /// </summary>
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

    /// <summary>Clicks the "Browse all" link in the Favourite Reports widget and waits for navigation.</summary>
    public async Task ClickFavouriteReportsBrowseAllAsync()
    {
        await FavouriteReportsWidget.Locator(".widget-view-all").ClickAsync();
        await page.WaitForURLAsync(new Regex(@"/companies/[0-9a-f-]{36}/reporting$"), new() { Timeout = 15_000 });
    }
}
