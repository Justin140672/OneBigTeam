using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the location list page (/companies/{companyId}/locations).
/// </summary>
public sealed class LocationListPage(IPage page, string baseUrl)
{
    // See DepartmentListPage for why row-selector waits (not just ".e-grid") are required:
    // Syncfusion's EJ2 grid populates ".e-row"/".e-rowcell" in a separate JS render pass.
    private const string RowsRenderedSelector = ".e-grid .e-row, .e-grid .e-emptyrow";

    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/locations");
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 20_000 });
    }

    public async Task ClickNewLocationAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Add" }).ClickAsync();
        await page.WaitForURLAsync("**/locations/new", new() { Timeout = 15_000 });
    }

    public async Task<bool> HasLocationAsync(string nameFragment)
    {
        await page.WaitForSelectorAsync(RowsRenderedSelector, new() { Timeout = 15_000 });
        return await page.Locator(".e-rowcell")
            .Filter(new() { HasText = nameFragment })
            .First
            .IsVisibleAsync();
    }
}
