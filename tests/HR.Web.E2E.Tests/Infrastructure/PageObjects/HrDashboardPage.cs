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
        // SfAccumulationChart renders its pie as an <svg>, not a ".e-accumulationchart-series"
        // element (that class doesn't exist in Syncfusion's actual DOM output) — "svg" is a
        // reliable signal the chart itself has rendered, regardless of its internal series markup.
        await widget.Locator("svg, .widget-empty").First
            .WaitForAsync(new() { Timeout = 15_000 });
    }

    // ── Gender Split Chart ───────────────────────────────────────────────────
    // GenderSplitChart.razor ("Gender Split") — same non-interactive
    // SfAccumulationChart pattern as the headcount chart above (renders an <svg>
    // once loaded, or ".widget-empty" with "No employee data available." when
    // there are no active employees).

    private ILocator GenderSplitWidget =>
        page.Locator(".widget-card").Filter(new() { HasText = "Gender Split" }).First;

    /// <summary>Waits for the Gender Split chart tile to finish loading (chart svg or empty state).</summary>
    public async Task WaitForGenderSplitChartLoadedAsync() =>
        await GenderSplitWidget.Locator("svg, .widget-empty").First
            .WaitForAsync(new() { Timeout = 15_000 });

    /// <summary>Returns true once loaded if the Gender Split widget is showing its empty state.</summary>
    public async Task<bool> GenderSplitChartIsEmptyAsync() =>
        await GenderSplitWidget.Locator(".widget-empty").IsVisibleAsync();

    // ── Employment Type Split Chart ──────────────────────────────────────────
    // EmploymentTypeSplitChart.razor ("Employment Type") — identical structure/behavior
    // to GenderSplitChart above, grouped by employment type instead of gender.

    private ILocator EmploymentTypeSplitWidget =>
        page.Locator(".widget-card").Filter(new() { HasText = "Employment Type" }).First;

    /// <summary>Waits for the Employment Type chart tile to finish loading (chart svg or empty state).</summary>
    public async Task WaitForEmploymentTypeSplitChartLoadedAsync() =>
        await EmploymentTypeSplitWidget.Locator("svg, .widget-empty").First
            .WaitForAsync(new() { Timeout = 15_000 });

    /// <summary>Returns true once loaded if the Employment Type widget is showing its empty state.</summary>
    public async Task<bool> EmploymentTypeSplitChartIsEmptyAsync() =>
        await EmploymentTypeSplitWidget.Locator(".widget-empty").IsVisibleAsync();

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
    /// <paramref name="nameFragment"/> (LeaveRequestsWidget.razor's OnRequestClicked). Only a
    /// Pending request is actionable — clicking it opens TaskViewDialog in place for its open
    /// leave-approval task (use TaskViewPage to interact with it). Any other status (Approved,
    /// Declined, Rejected, etc.) renders as a static, non-clickable row (CSS class
    /// "task-widget-item--static") with no dialog or navigation — clicking it is a deliberate
    /// no-op. Callers should assert on whichever outcome they expect.
    ///
    /// <paramref name="dateFragment"/> optionally narrows further by the row's rendered date
    /// range text (e.g. "14 Sep"). Several tests in this suite create more than one leave
    /// request for the same employee against the shared dev database (some deliberately left
    /// with an open task), so a name-only match can land on the wrong row — pass this whenever
    /// more than one request for the same employee might be visible.
    /// </summary>
    public async Task ClickLeaveRequestItemAsync(string nameFragment, string? dateFragment = null)
    {
        await LeaveRequestsWidget.Locator(".task-widget-item, .widget-empty").First
            .WaitForAsync(new() { Timeout = 15_000 });

        var locator = LeaveRequestsWidget.Locator(".task-widget-item")
            .Filter(new() { HasText = nameFragment });

        if (dateFragment is not null)
            locator = locator.Filter(new() { HasText = dateFragment });

        await locator.First.ClickAsync();
    }

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

    /// <summary>
    /// Clicks the first item in the Upcoming Probation Reviews widget. UpcomingProbationReviewsWidget.
    /// razor's OnReviewClicked opens the review's task dialog in place when one exists — which
    /// GenerateDueProbationReviewsJob always creates, so this is the normal case for any seeded
    /// review — falling back to navigating to the employee's Probation tab only for the rare
    /// review that predates task creation and has no open task. Does not itself wait for either
    /// outcome; callers should follow up with TaskViewPage.WaitForLoadedAsync() (dialog case) or
    /// page.WaitForURLAsync (navigation fallback case) as appropriate.
    /// </summary>
    public async Task ClickFirstUpcomingProbationReviewAsync()
    {
        await UpcomingProbationWidget.Locator(".task-widget-item, .widget-empty").First
            .WaitForAsync(new() { Timeout = 15_000 });

        await UpcomingProbationWidget.Locator(".task-widget-item").First.ClickAsync();
    }

    // ── Document Reviews Widget ────────────────────────────────────────────────
    // DocumentReviewsWidget.razor — gated on Session.CanManageEmployees (redundant with the
    // route guard, same as the sickness trio above), header title "Document Reviews".

    private ILocator DocumentReviewsWidget =>
        page.Locator(".widget-card").Filter(new() { HasText = "Document Reviews" }).First;

    /// <summary>Returns the document titles shown in the Document Reviews widget items.</summary>
    public async Task<IReadOnlyList<string>> GetDocumentReviewTitlesAsync()
    {
        await DocumentReviewsWidget.Locator(".task-widget-item, .widget-empty").First
            .WaitForAsync(new() { Timeout = 15_000 });

        var titles = await DocumentReviewsWidget.Locator(".task-widget-title").AllAsync();
        var names  = new List<string>();
        foreach (var t in titles)
            names.Add((await t.TextContentAsync())?.Trim() ?? "");
        return names;
    }

    /// <summary>
    /// Clicks the Document Reviews widget row whose title contains <paramref name="titleFragment"/>
    /// (DocumentReviewsWidget.razor navigates straight to the SharedDocumentDetail route on click,
    /// unlike LeaveRequestsWidget's dual outcome) and waits for navigation to
    /// "/companies/{companyId}/shared-documents/{documentId}".
    /// </summary>
    public async Task ClickDocumentReviewItemAsync(string titleFragment)
    {
        await DocumentReviewsWidget.Locator(".task-widget-item, .widget-empty").First
            .WaitForAsync(new() { Timeout = 15_000 });

        await DocumentReviewsWidget.Locator(".task-widget-item")
            .Filter(new() { HasText = titleFragment })
            .First
            .ClickAsync();
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
