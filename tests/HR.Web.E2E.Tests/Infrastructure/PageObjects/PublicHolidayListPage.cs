using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the public holiday list page (/companies/{companyId}/public-holidays).
/// </summary>
public sealed class PublicHolidayListPage(IPage page, string baseUrl)
{
    // Waiting for ".e-grid" alone is NOT sufficient to guarantee the toolbar's "Add" button is
    // interactive: Syncfusion's EJ2 grid does its own JS render pass to populate rows/toolbar into
    // the DOM on a separate tick after the Blazor component itself has mounted. Waiting for the
    // row selector (or its empty-state sibling) directly, plus the error state, is the only wait
    // that's actually tied to the grid being fully rendered — see the same pattern in
    // DepartmentListPage/EmployeeListPage/VacancyListPage etc.
    private const string RowsRenderedSelector = ".e-grid .e-row, .e-grid .e-emptyrow, .alert-danger";

    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/public-holidays");
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 20_000 });
    }

    public async Task ClickNewPublicHolidayAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Add" }).ClickAsync();
        await page.WaitForURLAsync("**/public-holidays/new**", new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Opens the edit page for a holiday by clicking its name link
    /// (<c>&lt;a href="/companies/{id}/public-holidays/{holidayId}"&gt;</c> in the Name column) and
    /// waits for the edit route + date picker to render.
    /// </summary>
    public async Task ClickHolidayAsync(string nameFragment)
    {
        // The grid pages client-side at 20 rows and the suite keeps adding "E2E …" holidays during
        // a run (several on far-future dates that sort late), so the target can be past page 1 —
        // page to it first (HasGridCellOnAnyPageAsync leaves the grid on the matching page).
        if (!await page.HasGridCellOnAnyPageAsync(nameFragment))
            throw new InvalidOperationException($"Public holiday '{nameFragment}' was not found on any page of the list.");

        await page.Locator(".e-grid a").Filter(new() { HasText = nameFragment }).First.ClickAsync();
        await page.WaitForURLAsync(
            new System.Text.RegularExpressions.Regex(@"/public-holidays/[0-9a-fA-F-]{36}$"),
            new() { Timeout = 15_000 });
        await page.WaitForSelectorAsync(".e-date-wrapper", new() { Timeout = 20_000 });
    }

    // Searches every grid page, not just the one currently displayed — see ClickHolidayAsync.
    public Task<bool> HasHolidayAsync(string nameFragment) =>
        page.HasGridCellOnAnyPageAsync(nameFragment);

    public async Task<IReadOnlyList<string>> GetHolidayNamesAsync()
    {
        var cells = await page.Locator(".e-rowcell").AllAsync();
        var names = new List<string>();
        foreach (var cell in cells)
        {
            var text = (await cell.TextContentAsync())?.Trim() ?? "";
            if (!string.IsNullOrEmpty(text))
                names.Add(text);
        }
        return names;
    }

}
