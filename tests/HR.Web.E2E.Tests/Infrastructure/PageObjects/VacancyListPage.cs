using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class VacancyListPage(IPage page, string baseUrl)
{
    private const string RowsRenderedSelector = ".e-grid .e-row, .e-grid .e-emptyrow";

    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/vacancies");
        await page.WaitForSelectorAsync(".app-shell", new() { Timeout = 30_000 });
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 30_000 });
    }

    public Task<bool> IsShowingActiveOnlyAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "Show Inactive" }).IsVisibleAsync();

    public async Task ShowAllVacanciesAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Show Inactive" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Show Active" }).WaitForAsync(
            new() { Timeout = 15_000 });
    }

    public async Task ClickNewVacancyAsync()
    {
        await page.ClickGridAddAndWaitForCreateRouteAsync("**/vacancies/new**");
        await page.GetByPlaceholder("e.g. Senior Software Engineer")
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
    }

    public async Task<bool> HasVacancyAsync(string titleFragment)
    {
        await SearchAsync(titleFragment);

        return await page.Locator(".e-rowcell")
            .Filter(new() { HasText = titleFragment })
            .First
            .WaitUntilVisibleAsync();
    }

    public async Task SearchAsync(string query)
    {
        var searchInput = page.GetByPlaceholder("Search by title or position profile");
        await searchInput.ClearAsync();
        await searchInput.FillAsync(query);
        await searchInput.PressAsync("Enter");
        await page.WaitForTimeoutAsync(400);
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
    }

    public async Task ClickVacancyAsync(string titleFragment)
    {
        await SearchAsync(titleFragment);

        var link = page.Locator(".e-rowcell a")
            .Filter(new() { HasText = titleFragment })
            .First;
        await link.ClickAsync();
        await page.WaitForURLAsync("**/vacancies/**", new() { Timeout = 30_000 });

        await page.WaitForSelectorAsync(".e-tab, span[role='combobox']", new() { Timeout = 20_000 });
    }

    public async Task<string> GetRowCellAsync(string titleFragment, int columnIndex)
    {
        await SearchAsync(titleFragment);

        var row = page.Locator(".e-row").Filter(new() { HasText = titleFragment }).First;
        return (await row.Locator(".e-rowcell").Nth(columnIndex).InnerTextAsync()).Trim();
    }

    public Task<string> GetPositionProfileColumnTextAsync(string titleFragment) =>
        GetRowCellAsync(titleFragment, columnIndex: 1);

    public Task<string> GetApplicationsColumnTextAsync(string titleFragment) =>
        GetRowCellAsync(titleFragment, columnIndex: 3);

    // NOTE: the Location column (VacancyList.razor's EffectiveLocation GridColumn) has no
    // override-vs-fallback distinction to indicate anymore — Vacancy.Location was removed
    // entirely as part of the "Vacancy - Position Profile relationship" epic's location
    // correction, so Location is now unconditionally just the linked Position Profile's location,
    // rendered as a plain field with no Template/indicator. A
    // HasLocationColumnPositionProfileFallbackIndicatorAsync method used to live here targeting
    // a fallback indicator that no longer exists in the markup; it was removed along with the
    // test that exercised it.
}
