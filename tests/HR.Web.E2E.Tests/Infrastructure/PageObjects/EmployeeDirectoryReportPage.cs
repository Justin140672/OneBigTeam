using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the Employee Directory report
/// (/companies/{companyId}/reporting/employee-directory — EmployeeDirectoryReportPage.razor).
/// </summary>
public sealed class EmployeeDirectoryReportPage(IPage page, string baseUrl)
{
    // Same reasoning as EmployeeListPage.RowsRenderedSelector — Syncfusion's EJ2 grid populates
    // ".e-row"/".e-rowcell" on a separate JS render pass after the Blazor component mounts, so
    // waiting for the row selector (or its empty-state sibling) is the only race-free wait tied
    // to data actually being present.
    private const string RowsRenderedSelector = ".e-grid .e-row, .e-grid .e-emptyrow";

    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/reporting/employee-directory");
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 20_000 });
    }

    public async Task<IReadOnlyList<string>> GetColumnHeadersAsync()
    {
        var headers = await page.Locator(".e-headercell").AllAsync();
        var result = new List<string>();
        foreach (var header in headers)
            result.Add((await header.TextContentAsync())?.Trim() ?? "");
        return result;
    }

    public Task<bool> HasColumnHeaderAsync(string headerText) =>
        page.Locator(".e-headercell").Filter(new() { HasText = headerText }).First.IsVisibleAsync();

    public async Task<int> GetRowCountAsync()
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
        // An empty grid renders a single ".e-emptyrow" placeholder, not a real data row.
        if (await page.Locator(".e-grid .e-emptyrow").CountAsync() > 0)
            return 0;
        return await page.Locator(".e-grid .e-row").CountAsync();
    }

    /// <summary>The page's own "N employee(s)" total count summary, shown below the grid.</summary>
    public async Task<string?> GetTotalCountTextAsync()
    {
        var summary = page.Locator(".d-flex.justify-content-between.align-items-center div")
            .Filter(new() { HasTextRegex = new System.Text.RegularExpressions.Regex("employee\\(s\\)") })
            .First;
        return (await summary.TextContentAsync())?.Trim();
    }

    // ── Filter panel (ReportFilterPanel) ──────────────────────────────────────

    private ILocator FilterField(string labelText) =>
        page.Locator(".card-body .col-md-3").Filter(new() { HasText = labelText }).First;

    /// <summary>
    /// Selects <paramref name="valueText"/> in the filter field labelled <paramref name="labelText"/>
    /// (e.g. "Department", "Status") via the shared DropDownSelector — never hand-rolled.
    /// </summary>
    public async Task SelectFilterAsync(string labelText, string valueText)
    {
        await DropDownSelector.SelectAsync(page, FilterField(labelText), valueText);
    }

    public async Task ApplyFiltersAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Apply Filters" }).ClickAsync();
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }

    public async Task ClearFiltersAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Clear" }).ClickAsync();
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }

    // ── Sort ───────────────────────────────────────────────────────────────────

    public async Task SelectSortByAsync(string label)
    {
        var sortField = page.Locator("label.form-label").Filter(new() { HasText = "Sort by" }).Locator("xpath=..");
        await DropDownSelector.SelectAsync(page, sortField, label);
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }

    public async Task ToggleSortDirectionAsync()
    {
        await page.Locator("i.fa-arrow-down, i.fa-arrow-up").Locator("xpath=ancestor::button[1]").ClickAsync();
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }

    // ── Paging ─────────────────────────────────────────────────────────────────

    private ILocator PreviousButton => page.GetByRole(AriaRole.Button, new() { Name = "Previous" });
    private ILocator NextButton => page.GetByRole(AriaRole.Button, new() { Name = "Next" });

    public Task<bool> IsPreviousDisabledAsync() => PreviousButton.IsDisabledAsync();
    public Task<bool> IsNextDisabledAsync() => NextButton.IsDisabledAsync();

    public async Task ClickNextAsync()
    {
        await NextButton.ClickAsync();
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }

    public async Task ClickPreviousAsync()
    {
        await PreviousButton.ClickAsync();
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }

    /// <summary>The "Page X of Y" pager status text.</summary>
    public async Task<string?> GetPagerStatusTextAsync()
    {
        var status = page.Locator("span").Filter(new() { HasTextRegex = new System.Text.RegularExpressions.Regex("^Page \\d+ of \\d+$") }).First;
        return (await status.TextContentAsync())?.Trim();
    }

    // ── Export ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Opens the Export SfDropDownButton and clicks the item matching <paramref name="formatLabel"/>
    /// (e.g. "CSV"/"Excel"/"PDF"), returning the triggered browser download.
    /// </summary>
    public async Task<IDownload> ExportAsync(string formatLabel)
    {
        var downloadTask = page.WaitForDownloadAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Export" }).ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = formatLabel }).ClickAsync();
        return await downloadTask;
    }

    public async Task<string?> GetExportErrorMessageAsync()
    {
        var banner = page.Locator(".alert-danger");
        return await banner.IsVisibleAsync() ? (await banner.TextContentAsync())?.Trim() : null;
    }

    /// <summary>True if the page rendered its own graceful error banner rather than crashing (e.g. on a 403 from the report data endpoint).</summary>
    public async Task<bool> HasLoadErrorAsync() => await page.Locator(".alert-danger").IsVisibleAsync();
}
