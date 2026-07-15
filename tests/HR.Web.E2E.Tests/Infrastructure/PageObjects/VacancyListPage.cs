using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the vacancy list page (/companies/{companyId}/vacancies).
/// </summary>
public sealed class VacancyListPage(IPage page, string baseUrl)
{
    // Waiting for ".e-grid" alone (or for the Blazor-side loading spinner to clear) is NOT
    // sufficient to guarantee rows are queryable: Syncfusion's EJ2 grid does its own JS render
    // pass to populate ".e-row"/".e-rowcell" into the DOM on a separate tick after the Blazor
    // component itself has mounted. Waiting for the row selector (or its empty-state sibling)
    // directly is the only wait that's actually tied to the data being present.
    private const string RowsRenderedSelector = ".e-grid .e-row, .e-grid .e-emptyrow";

    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/vacancies");
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 20_000 });
    }

    public async Task ClickNewVacancyAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Add" }).ClickAsync();
        await page.WaitForURLAsync("**/vacancies/new", new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Waits for the grid's rows to actually be rendered, then checks whether a row with this
    /// title is present. Callers that navigate here via something other than GoToAsync (e.g.
    /// clicking a dashboard widget) won't have already waited for this, so checking immediately
    /// on arrival can race the load and report false negatives while rows are still populating.
    /// </summary>
    public async Task<bool> HasVacancyAsync(string titleFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

        return await page.Locator(".e-rowcell")
            .Filter(new() { HasText = titleFragment })
            .First
            .IsVisibleAsync();
    }

    public async Task ClickVacancyAsync(string titleFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });

        var link = page.Locator(".e-rowcell a")
            .Filter(new() { HasText = titleFragment })
            .First;
        await link.ClickAsync();
        await page.WaitForURLAsync("**/vacancies/**", new() { Timeout = 15_000 });
    }
}
