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
        var searchInput = page.GetByPlaceholder("Search by name or email");
        await searchInput.ClearAsync();
        await searchInput.FillAsync(query);
        // Syncfusion grid filters on input change.
        await page.WaitForTimeoutAsync(500);
    }
}
