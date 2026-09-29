using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class PublicHolidayListPage(IPage page, string baseUrl)
{
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

    public async Task ClickHolidayAsync(string nameFragment)
    {
        if (!await page.HasGridCellOnAnyPageAsync(nameFragment))
            throw new InvalidOperationException($"Public holiday '{nameFragment}' was not found on any page of the list.");

        await page.Locator(".e-grid a").Filter(new() { HasText = nameFragment }).First.ClickAsync();
        await page.WaitForURLAsync(
            new System.Text.RegularExpressions.Regex(@"/public-holidays/[0-9a-fA-F-]{36}$"),
            new() { Timeout = 15_000 });
        await page.WaitForSelectorAsync(".e-date-wrapper", new() { Timeout = 20_000 });
    }

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
