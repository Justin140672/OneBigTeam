using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the employee list page (/companies/{companyId}/employees).
/// </summary>
public sealed class EmployeeListPage(IPage page, string baseUrl)
{
    // Waiting for ".e-grid" alone is NOT sufficient to guarantee rows are queryable: Syncfusion's
    // EJ2 grid does its own JS render pass to populate ".e-row"/".e-rowcell" into the DOM on a
    // separate tick after the Blazor component itself has mounted. Waiting for the row selector
    // (or its empty-state sibling) directly is the only wait that's actually tied to data being
    // present — see the same pattern in VacancyListPage etc.
    private const string RowsRenderedSelector = ".e-grid .e-row, .e-grid .e-emptyrow";

    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/employees");
        // Previously this tried to confirm the circuit had connected by watching for a
        // spinner→grid transition via a MutationObserver installed with page.EvaluateAsync
        // *after* navigation. That's a race: if Blazor's prerender→interactive spinner cycle
        // finishes before the observer script gets installed (routine on a fast/local run),
        // the transition is never observed, window._listReady never flips true, and the wait
        // times out — which made most tests starting with GoToAsync fail. RowsRenderedSelector
        // alone is sufficient and race-free: Syncfusion can only populate real ".e-row"/
        // ".e-rowcell" data via its JS interop once the interactive circuit is connected and
        // the component's data fetch has completed, so waiting for it already proves both.
        // Same pattern as VacancyListPage/PublicHolidayListPage etc.
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 20_000 });
    }

    public async Task ClickNewEmployeeAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Add" }).ClickAsync();
        await page.WaitForURLAsync("**/employees/new", new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Returns true if an employee matching <paramref name="nameFragment"/> exists, searching for
    /// it via the page's own search box rather than scanning whatever's on the current unfiltered
    /// page. EmployeeList.razor loads an unfiltered page capped at 100 rows sorted by last name —
    /// on this shared, long-lived E2E database that cap is easy to exceed, so a specific employee
    /// (e.g. one a test just created) can silently fall outside it with no indication why. The
    /// search box round-trips to the server (SearchPageBase.OnSearchChanged), so it finds the
    /// employee regardless of how many others sort before them.
    /// </summary>
    public async Task<bool> HasEmployeeAsync(string nameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

        await page.GetByPlaceholder("Search by name, email or employee number").FillAsync(nameFragment);
        // OnSearchChanged debounces 300ms before reloading — wait past that, then for the grid to
        // settle on the filtered result (row or empty state) rather than the pre-search rows.
        await page.WaitForTimeoutAsync(400);
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

        return await page.Locator(".e-rowcell")
            .Filter(new() { HasText = nameFragment })
            .First
            .IsVisibleAsync();
    }

    public async Task<IReadOnlyList<string>> GetEmployeeNamesAsync()
    {
        var cells = await page.Locator(".e-rowcell a").AllAsync();
        var names = new List<string>();
        foreach (var cell in cells)
            names.Add((await cell.TextContentAsync())?.Trim() ?? "");
        return names;
    }

    /// <summary>
    /// Checks the row-selection checkbox (GridColumn Type="CheckBox") for the employee whose row
    /// contains <paramref name="nameFragment"/>. Clicking the Syncfusion checkbox's own wrapper
    /// span (rather than the underlying hidden native input) mirrors a real user click and is the
    /// documented way to toggle a grid checkbox column.
    /// </summary>
    public async Task CheckEmployeeRowAsync(string nameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

        var row = page.Locator(".e-grid .e-row").Filter(new() { HasText = nameFragment }).First;
        await row.Locator(".e-checkbox-wrapper").First.ClickAsync();
    }

    /// <summary>
    /// Returns true if the "Bulk Update" toolbar dropdown button (id "hr-bulk-update", added via
    /// EmployeeList.ConfigureToolbar, now rendered as a BulkUpdateMenu SfDropDownButton via
    /// EmployeeList.EmployeeToolbar) is currently disabled — it tracks row selection the same way
    /// as the built-in Edit/View actions (SearchPageBase.OnRowSelected/OnRowDeselected), so it's
    /// disabled with zero rows selected and enabled once 1+ rows are selected.
    /// </summary>
    public async Task<bool> IsBulkUpdateButtonDisabledAsync()
    {
        var btn = page.GetByRole(AriaRole.Button, new() { Name = "Bulk Update" });
        return await btn.IsDisabledAsync();
    }

    /// <summary>
    /// Opens the "Bulk Update" toolbar dropdown (BulkUpdateMenu) and clicks its "Selected
    /// Employees" item, reaching BulkCompensationUpdateDialog for whichever row(s) are currently
    /// checked — the same destination the old plain "Bulk Update" button used to open directly.
    /// Waits for the SfDialog (identified by its own CssClass, scoped to the role="dialog" element
    /// to avoid matching any other node that shares the class — see the Playwright locator
    /// conventions note re: Syncfusion CssClass reuse) to become visible.
    /// </summary>
    public async Task ClickBulkUpdateAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Bulk Update" }).ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "Selected Employees" }).ClickAsync();
        await page.WaitForSelectorAsync(
            "[role='dialog'].bulk-compensation-update-dialog",
            new() { Timeout = 15_000 });
    }

    /// <summary>
    /// The page's own success banner (_actionSuccess), shown after a bulk update dialog applies
    /// successfully and closes (see EmployeeList.HandleBulkUpdateApplied).
    /// </summary>
    public async Task<string?> GetActionSuccessMessageAsync()
    {
        var banner = page.Locator(".alert-success");
        return await banner.IsVisibleAsync() ? (await banner.TextContentAsync())?.Trim() : null;
    }

    /// <summary>
    /// The page's own error banner (_actionError), e.g. shown if downloading the compensation
    /// import template fails.
    /// </summary>
    public async Task<string?> GetActionErrorMessageAsync()
    {
        var banner = page.Locator(".alert-danger");
        return await banner.IsVisibleAsync() ? (await banner.TextContentAsync())?.Trim() : null;
    }

    /// <summary>
    /// Opens the "Bulk Update" toolbar dropdown (BulkUpdateMenu) and clicks "Download Template",
    /// triggering a browser download of the compensation import template — mirrors
    /// BulkCompensationUpdatePage.ClickDownloadTemplateAsync for the full-page equivalent.
    /// </summary>
    public async Task<string> ClickDownloadTemplateAsync()
    {
        var downloadTask = page.WaitForDownloadAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Bulk Update" }).ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "Download Template" }).ClickAsync();
        var download = await downloadTask;
        return download.SuggestedFilename;
    }

    /// <summary>
    /// Opens the "Bulk Update" toolbar dropdown and clicks "Import", reaching
    /// BulkCompensationImportDialog (identified by its own CssClass,
    /// "bulk-compensation-import-dialog") for the Import from Excel flow.
    /// </summary>
    public async Task ClickBulkImportAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Bulk Update" }).ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "Import", Exact = true }).ClickAsync();
        await page.WaitForSelectorAsync(
            "[role='dialog'].bulk-compensation-import-dialog",
            new() { Timeout = 15_000 });
    }

    public async Task ClickEmployeeAsync(string nameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

        var link = page.Locator(".e-rowcell a")
            .Filter(new() { HasText = nameFragment })
            .First;
        await link.ClickAsync();
        await page.WaitForURLAsync("**/employees/**", new() { Timeout = 15_000 });
        // The edit page shows a spinner while its LoadAsync() runs; without this wait, callers
        // that immediately assert on page content (e.g. tab visibility) can race the load and
        // observe the page still in its loading state.
        await page.WaitForSelectorAsync("[role='tablist']", new() { Timeout = 15_000 });
    }

    public async Task SearchAsync(string query)
    {
        // EmployeeList.razor's search placeholder is "Search by name, email or employee number"
        // (see EmployeeList.razor) — matches HasEmployeeAsync's placeholder text above.
        var searchInput = page.GetByPlaceholder("Search by name, email or employee number");
        await searchInput.ClearAsync();
        await searchInput.FillAsync(query);
        // OnSearchChanged debounces 300ms before reloading — wait past that, then for the grid to
        // settle on the filtered result (row or empty state) rather than the pre-search rows.
        await page.WaitForTimeoutAsync(400);
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }
}
