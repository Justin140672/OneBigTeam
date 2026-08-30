using HR.Web.E2E.Tests.Infrastructure;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the ADM-02 Compliance Centre
/// (/companies/{companyId}/reporting/compliance-centre — ComplianceCentrePage.razor).
///
/// The page gates on <c>AppSession.CanViewComplianceCentre</c> (HR Administrator only; other
/// personas are client-side redirected to /access-denied by AppSession.GuardAccess).
///
/// Layout: four summary count cards (Total / Overdue / Due soon / Informational) in the first
/// <c>div.row.g-3.mb-3</c>, a "Consolidated sections" per-category breakdown table (6 rows), a
/// filters card with Syncfusion SfDropDownList for Category / Severity / Department / Manager and
/// two SfDatePicker for the due-date range, an <c>HrGrid</c> of items with an "Open" drill-through
/// button per row (navigates via the server-provided relative <c>DeepLinkUrl</c>), a green
/// "No compliance action required" alert when <c>NoActionRequired</c>, and a truncation notice.
///
/// Unlike the sibling report pages there is NO "Apply Filters" button — each filter's
/// <c>ValueChanged</c> triggers a reload directly — and there are no explicit "All ..." list items
/// in the dropdowns; the only reset affordance is the "Clear" button.
/// </summary>
public sealed class ComplianceCentrePage(IPage page, string baseUrl)
{
    // Once loading finishes the page shows either grid rows, an empty grid, the green
    // "no action required" alert, or the red load-error alert.
    private const string LoadedSelector =
        ".e-grid .e-row, .e-grid .e-emptyrow, .alert-success, .alert-danger";

    private static readonly string[] ExpectedCategoryLabels =
    [
        "Expiring visa",
        "Expiring certification",
        "Expiring other document",
        "Missing required document",
        "Outstanding document request",
        "Probation review",
    ];

    public static IReadOnlyList<string> CategoryLabels => ExpectedCategoryLabels;

    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/reporting/compliance-centre");
        await page.WaitForSelectorAsync(LoadedSelector, new() { Timeout = 20_000 });
    }

    public string Url => page.Url;

    public async Task<bool> HasLoadErrorAsync() => await page.Locator(".alert-danger").IsVisibleAsync();

    // ── Summary count cards ────────────────────────────────────────────────────

    // Scope to the first row so "Total" can't accidentally match the "Total" column header in the
    // per-category breakdown table further down the page.
    private ILocator SummaryRow => page.Locator("div.row.g-3.mb-3").First;

    private ILocator SummaryCard(string label) =>
        SummaryRow.Locator(".card").Filter(new() { HasText = label }).First;

    public async Task<bool> HasSummaryCardAsync(string label) =>
        await SummaryCard(label).Locator(".fs-4").IsVisibleAsync();

    /// <summary>The numeric value shown on a summary card, or -1 when it isn't a number.</summary>
    public async Task<int> GetSummaryValueAsync(string label)
    {
        var text = await SummaryCard(label).Locator(".fs-4").TextContentAsync();
        return int.TryParse(text?.Trim(), out var value) ? value : -1;
    }

    // ── Per-category breakdown table ───────────────────────────────────────────

    private ILocator BreakdownCard =>
        page.Locator(".card").Filter(new() { HasText = "Consolidated sections" }).First;

    private ILocator BreakdownRows => BreakdownCard.Locator("tbody tr");

    public async Task<int> GetBreakdownRowCountAsync() => await BreakdownRows.CountAsync();

    /// <summary>The first-cell (Category) label of every breakdown row, in order.</summary>
    public async Task<IReadOnlyList<string>> GetBreakdownCategoryLabelsAsync()
    {
        var cells = await BreakdownRows.Locator("td:first-child").AllAsync();
        var result = new List<string>();
        foreach (var cell in cells)
            result.Add((await cell.TextContentAsync())?.Trim() ?? "");
        return result;
    }

    // ── Filters ────────────────────────────────────────────────────────────────

    private ILocator FilterField(string label) =>
        page.Locator(".card-body .col-md-3").Filter(new() { HasText = label }).First;

    public async Task SelectCategoryAsync(string categoryLabel)
    {
        await DropDownSelector.SelectAsync(page, FilterField("Category"), categoryLabel);
        await WaitForReloadAsync();
    }

    public async Task SelectSeverityAsync(string severityLabel)
    {
        await DropDownSelector.SelectAsync(page, FilterField("Severity"), severityLabel);
        await WaitForReloadAsync();
    }

    /// <summary>
    /// Resets every filter. The dropdowns carry no explicit "All ..." list item, so the page's
    /// "Clear" button is the only reset affordance.
    /// </summary>
    public async Task ClearFiltersAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Clear" }).ClickAsync();
        await WaitForReloadAsync();
    }

    private async Task WaitForReloadAsync()
    {
        // Each ValueChanged flips _isLoading true (HrLoadingIndicator) then back to false with the
        // new results. Wait for the loaded state again, then let the re-render settle a tick — the
        // same "selector can resolve against stale pre-reload content" race the sibling report page
        // objects guard with a short fixed wait after their Apply/Clear.
        await page.WaitForSelectorAsync(LoadedSelector, new() { Timeout = 20_000 });
        await page.WaitForTimeoutAsync(400);
    }

    // ── Items grid ─────────────────────────────────────────────────────────────

    public async Task<bool> IsEmptyStateVisibleAsync() =>
        await page.Locator(".alert-success", new() { HasText = "No compliance action required" })
            .IsVisibleAsync();

    public async Task<int> GetRowCountAsync()
    {
        await page.WaitForSelectorAsync(LoadedSelector, new() { Timeout = 15_000 });
        if (await IsEmptyStateVisibleAsync())
            return 0;
        if (await page.Locator(".e-grid .e-emptyrow").CountAsync() > 0)
            return 0;
        return await page.Locator(".e-grid .e-row").CountAsync();
    }

    public async Task<IReadOnlyList<string>> GetColumnHeadersAsync()
    {
        var headers = await page.Locator(".e-grid .e-headercell").AllAsync();
        var result = new List<string>();
        foreach (var header in headers)
            result.Add((await header.TextContentAsync())?.Trim() ?? "");
        return result;
    }

    /// <summary>The visible cell text of the first data row, or an empty list when there are none.</summary>
    public async Task<IReadOnlyList<string>> GetFirstRowCellsAsync()
    {
        if (await GetRowCountAsync() == 0)
            return [];
        var cells = await page.Locator(".e-grid .e-row").First.Locator(".e-rowcell").AllAsync();
        var result = new List<string>();
        foreach (var cell in cells)
            result.Add((await cell.TextContentAsync())?.Trim() ?? "");
        return result;
    }

    private ILocator FirstRowOpenButton =>
        page.Locator(".e-grid .e-row").First.GetByRole(AriaRole.Button, new() { Name = "Open" });

    public async Task<bool> HasDrillThroughLinkAsync() => await FirstRowOpenButton.CountAsync() > 0;

    /// <summary>
    /// Clicks the first row's "Open" drill-through button and waits for the resulting client-side
    /// navigation (Navigation.NavigateTo(row.DeepLinkUrl)) to leave the compliance-centre URL.
    /// </summary>
    public async Task ClickFirstRowDrillThroughAsync()
    {
        await FirstRowOpenButton.ClickAsync();
        await page.WaitForURLAsync(url => !url.Contains("/reporting/compliance-centre"),
            new() { Timeout = 15_000 });
    }
}
