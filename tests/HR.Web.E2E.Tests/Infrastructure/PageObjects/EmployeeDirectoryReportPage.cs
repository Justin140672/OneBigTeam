using HR.Web.E2E.Tests.Infrastructure;
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

    // ── Sort (native SfGrid click-to-sort column headers) ───────────────────────

    private ILocator HeaderCell(string headerText) =>
        page.Locator(".e-headercell").Filter(new() { HasText = headerText }).First;

    /// <summary>
    /// Clicks the grid's column header for <paramref name="headerText"/> to sort by it (Syncfusion's
    /// native click-to-sort UI, replacing the removed "Sort by" dropdown). Clicking the same header
    /// again toggles the sort direction, matching Syncfusion's default behavior.
    /// </summary>
    public async Task SortByColumnAsync(string headerText)
    {
        await HeaderCell(headerText).ClickAsync();
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Returns "ascending"/"descending" for the header matching <paramref name="headerText"/> based
    /// on Syncfusion's <c>aria-sort</c> attribute (version-independent, unlike CSS class names), or
    /// null if the column isn't currently sorted.
    /// </summary>
    public async Task<string?> GetSortDirectionAsync(string headerText) =>
        await HeaderCell(headerText).GetAttributeAsync("aria-sort") switch
        {
            "ascending" => "ascending",
            "descending" => "descending",
            _ => null,
        };

    // ── Paging (native SfGrid pager) ─────────────────────────────────────────

    private ILocator NextPageButton => page.Locator(".e-pagercontainer .e-nextpage");
    private ILocator PreviousPageButton => page.Locator(".e-pagercontainer .e-prevpage");
    private ILocator CurrentPageItem => page.Locator(".e-pagercontainer .e-numericitem.e-currentitem");

    public async Task<bool> IsNextPageDisabledAsync() =>
        await NextPageButton.GetAttributeAsync("aria-disabled") == "true";

    public async Task<bool> IsPreviousPageDisabledAsync() =>
        await PreviousPageButton.GetAttributeAsync("aria-disabled") == "true";

    public async Task ClickNextPageAsync()
    {
        await NextPageButton.ClickAsync();
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }

    public async Task ClickPreviousPageAsync()
    {
        await PreviousPageButton.ClickAsync();
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }

    /// <summary>The active page number, read from Syncfusion's highlighted pager item.</summary>
    public async Task<int> GetCurrentPageNumberAsync()
    {
        var text = (await CurrentPageItem.TextContentAsync())?.Trim() ?? "1";
        return int.Parse(text);
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

    // ── Saved Views (ReportFilterPanel) ─────────────────────────────────────────

    private ILocator SavedViewsField =>
        page.Locator(".card-body .col-md-4").Filter(new() { HasText = "Saved Views" }).First;

    /// <summary>
    /// Selects <paramref name="viewNameOrDisplayText"/> in the "Saved Views" dropdown via the
    /// shared DropDownSelector — never hand-rolled. Selecting a view re-applies its saved filters
    /// (OnSavedViewSelectedAsync in ReportFilterPanel.razor).
    /// </summary>
    public async Task SelectSavedViewAsync(string viewNameOrDisplayText)
    {
        await DropDownSelector.SelectAsync(page, SavedViewsField, viewNameOrDisplayText);
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Opens the "Saved Views" dropdown popup and returns the visible option labels (e.g.
    /// "My View" or "My View (Default)" for the default view), then closes the popup again
    /// without selecting anything.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetSavedViewOptionTextsAsync()
    {
        var combobox = SavedViewsField.Locator("span[role='combobox']").First;
        await combobox.ClickAsync();
        await page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });

        var items = await page.Locator(".e-popup.e-ddl .e-list-item").AllAsync();
        var result = new List<string>();
        foreach (var item in items)
            result.Add((await item.TextContentAsync())?.Trim() ?? "");

        await page.Keyboard.PressAsync("Escape");
        return result;
    }

    /// <summary>Clicks "Save current filters as view", fills the "View name" textbox, then clicks "Save".</summary>
    public async Task SaveCurrentFiltersAsNewViewAsync(string name)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save current filters as view" }).ClickAsync();
        await page.GetByPlaceholder("View name").FillAsync(name);
        await page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }

    /// <summary>Clicks "Rename" on the currently selected saved view, fills the new name, then clicks "Save Name".</summary>
    public async Task RenameSelectedViewAsync(string newName)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Rename", Exact = true }).ClickAsync();
        await page.GetByPlaceholder("View name").FillAsync(newName);
        await page.GetByRole(AriaRole.Button, new() { Name = "Save Name" }).ClickAsync();
    }

    /// <summary>Clicks "Set Default" for the currently selected saved view.</summary>
    public Task SetSelectedViewAsDefaultAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "Set Default" }).ClickAsync();

    /// <summary>Clicks "Delete" for the currently selected saved view.</summary>
    public Task DeleteSelectedViewAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "Delete", Exact = true }).ClickAsync();

    /// <summary>
    /// The saved-views error banner text (ReportFilterPanel.razor's own alert-danger, distinct
    /// from the grid-load error banner reused by <see cref="HasLoadErrorAsync"/>), or null if not present.
    /// </summary>
    public async Task<string?> GetSavedViewErrorAsync()
    {
        var banner = page.Locator(".card-body .alert-danger");
        return await banner.IsVisibleAsync() ? (await banner.TextContentAsync())?.Trim() : null;
    }
}
