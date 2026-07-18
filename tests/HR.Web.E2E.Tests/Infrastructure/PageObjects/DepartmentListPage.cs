using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the department list page (/companies/{companyId}/departments).
/// </summary>
public sealed class DepartmentListPage(IPage page, string baseUrl)
{
    // Waiting for ".e-grid" alone is NOT sufficient to guarantee rows are queryable: Syncfusion's
    // EJ2 grid does its own JS render pass to populate ".e-row"/".e-rowcell" into the DOM on a
    // separate tick after the Blazor component itself has mounted. Waiting for the row selector
    // (or its empty-state sibling) directly is the only wait that's actually tied to data being
    // present — see the same pattern in EmployeeListPage/VacancyListPage etc.
    private const string RowsRenderedSelector = ".e-grid .e-row, .e-grid .e-emptyrow";

    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/departments");
        // Previously this tried to confirm the circuit had connected by watching for a
        // spinner→grid transition via a MutationObserver installed with page.EvaluateAsync
        // *after* navigation. That's a race: if Blazor's prerender→interactive spinner cycle
        // finishes before the observer script gets installed (routine on a fast/local run), the
        // transition is never observed, window._listReady never flips true, and the wait times
        // out — which made most tests starting with GoToAsync fail. RowsRenderedSelector alone
        // is sufficient and race-free: Syncfusion can only populate real ".e-row"/".e-rowcell"
        // data via its JS interop once the interactive circuit is connected and the component's
        // data fetch has completed, so waiting for it already proves both.
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 20_000 });
    }

    public async Task ClickNewDepartmentAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Add" }).ClickAsync();
        await page.WaitForURLAsync("**/departments/new", new() { Timeout = 15_000 });
    }

    public async Task<bool> HasDepartmentAsync(string nameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
        return await page.Locator(".e-rowcell")
            .Filter(new() { HasText = nameFragment })
            .First
            .IsVisibleAsync();
    }

    public async Task<IReadOnlyList<string>> GetDepartmentNamesAsync()
    {
        var cells = await page.Locator(".e-rowcell a").AllAsync();
        var names = new List<string>();
        foreach (var cell in cells)
            names.Add((await cell.TextContentAsync())?.Trim() ?? "");
        return names;
    }

    /// <summary>
    /// Deactivates the department whose row contains <paramref name="nameFragment"/>
    /// by clicking the deactivate toolbar action.
    /// </summary>
    public async Task DeactivateDepartmentAsync(string nameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

        // Select the row first, then click the deactivate toolbar button.
        var row = page.Locator(".e-row")
            .Filter(new() { HasText = nameFragment })
            .First;
        await row.ClickAsync();
        // Blazor re-renders the toolbar after row selection; wait for the button to be enabled
        // (same pattern as EmploymentTypeListPage.DeactivateAsync — a title-attribute selector
        // doesn't reliably resolve here since the toolbar button isn't guaranteed to expose one).
        var btn = page.GetByRole(AriaRole.Button, new() { Name = "Deactivate" });
        await btn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await btn.ClickAsync();
        // Wait for the grid to refresh.
        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    public async Task<bool> IsActiveAsync(string nameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

        var row = page.Locator(".e-row")
            .Filter(new() { HasText = nameFragment })
            .First;
        var badge = row.Locator(".status-badge.status-badge--success");
        return await badge.IsVisibleAsync();
    }

    public async Task ShowInactiveAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Show Inactive" }).ClickAsync();
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }
}
