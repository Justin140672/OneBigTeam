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

        // The grid only mounts once the page's initial load finishes (_isLoading flips false),
        // and Syncfusion's JS binds its native click-to-sort/pager handlers a tick after that DOM
        // appears. A header/pager click that lands in that gap is silently swallowed — no error,
        // just no effect — so give interop a moment to finish binding before any caller interacts
        // with sort or paging (same race as DropDownSelector.SelectAsync's retry, but here there's
        // no popup to detect and retry against, so a short settle wait is the pragmatic fix).
        await page.WaitForTimeoutAsync(300);
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
        // Waiting for rows alone doesn't prove the sort actually applied — they're already
        // present from the page's initial (unsorted) load, so that wait resolves instantly and
        // can race this specific header's aria-sort attribute update, which lands in a separate
        // DOM mutation. Wait for THIS header's own aria-sort to actually change instead — a bare
        // "some header is sorted" check would be a false positive on the second call of a
        // click-twice-to-toggle sequence, where a different (or the same, already-ascending)
        // header can already satisfy it before the toggle to descending lands.
        var before = await HeaderCell(headerText).GetAttributeAsync("aria-sort");
        await HeaderCell(headerText).ClickAsync();
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var current = await HeaderCell(headerText).GetAttributeAsync("aria-sort");
            if (current != before) return;
            await page.WaitForTimeoutAsync(100);
        }
    }

    /// <summary>
    /// Clicks <paramref name="headerText"/> until it's unsorted (Syncfusion's default sort cycle is
    /// ascending → descending → unsorted, not a two-state ascending/descending toggle) — a starting
    /// point callers can rely on rather than assuming a freshly-navigated page is already unsorted.
    /// On this shared, long-lived E2E database a previous test/run can leave a column sorted, and a
    /// full page navigation doesn't necessarily reset it if the grid's sort state is itself
    /// persisted (e.g. a saved report view). Bounded to 3 clicks — the length of the cycle — so a
    /// column that's stuck sorted for some other reason fails fast instead of looping forever.
    /// </summary>
    public async Task ResetSortAsync(string headerText)
    {
        for (var i = 0; i < 3 && await GetSortDirectionAsync(headerText) is not null; i++)
        {
            await SortByColumnAsync(headerText);
        }
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

    /// <summary>
    /// True if the grid rendered a pager at all — Syncfusion's SfGrid doesn't render
    /// ".e-pagercontainer" when every row already fits on one page (this shared, long-lived
    /// E2E database's Acme employee count is close enough to PageSize that this varies run to
    /// run). Callers should check this before IsNextPageDisabledAsync/IsPreviousPageDisabledAsync
    /// — those use a bare GetAttributeAsync, which waits Playwright's full default actionability
    /// timeout (30s) for an element that will never exist at all when there's no pager, rather
    /// than failing fast.
    /// </summary>
    public async Task<bool> IsPagerVisibleAsync() =>
        await page.Locator(".e-pagercontainer").IsVisibleAsync();

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

    /// <summary>
    /// The "Save current filters as view" modal dialog (ReportFilterPanel.razor's
    /// CssClass="report-save-view-dialog" SfDialog, IsModal="true") — opened by clicking the "Save
    /// current filters as view" button, distinct from the older inline expand-row pattern this
    /// replaced.
    /// </summary>
    public ILocator SaveViewDialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Save current filters as view" });

    /// <summary>Clicks "Save current filters as view" and waits for the resulting modal dialog to open.</summary>
    public async Task OpenSaveViewDialogAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save current filters as view" }).ClickAsync();
        await SaveViewDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    /// <summary>Clicks "Save current filters as view", fills the "View name" textbox, then clicks "Save".</summary>
    public async Task SaveCurrentFiltersAsNewViewAsync(string name)
    {
        await OpenSaveViewDialogAsync();
        await page.GetByPlaceholder("View name").FillAsync(name);
        // The Save button's Disabled state is bound to the HrTextBox's server-side value
        // (ReportFilterPanel.razor's _newViewName), which only round-trips on blur/change — not
        // on FillAsync's raw "input" DOM event — so the button stays disabled until the field is
        // blurred. Same convention as this suite's date/numeric inputs (see e.g.
        // BulkCompensationUpdateDialogPage, AmendLeavingProcessDialog).
        await page.Keyboard.PressAsync("Tab");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await SaveViewDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }

    /// <summary>Clicks "Rename" on the currently selected saved view, fills the new name, then clicks "Save Name".</summary>
    public async Task RenameSelectedViewAsync(string newName)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Rename", Exact = true }).ClickAsync();
        await page.GetByPlaceholder("View name").FillAsync(newName);
        // See SaveCurrentFiltersAsNewViewAsync — the "Save Name" button is likewise disabled until
        // the field's value round-trips server-side, which needs a blur/change, not just FillAsync.
        await page.Keyboard.PressAsync("Tab");
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
