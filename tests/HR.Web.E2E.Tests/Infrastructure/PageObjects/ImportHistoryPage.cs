using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the import history list, showing past employee data import sessions for a
/// company.
/// </summary>
public sealed class ImportHistoryPage(IPage page, string baseUrl)
{
    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/data-import/history");
        await page.WaitForSelectorAsync(".e-grid, text=No import sessions yet.", new() { Timeout = 20_000 });
    }

    public async Task<bool> HasSessionAsync(string fileNameFragment) =>
        await page.Locator(".e-rowcell").Filter(new() { HasText = fileNameFragment }).First.IsVisibleAsync();

    public async Task OpenSessionAsync(string fileNameFragment)
    {
        await page.GetByRole(AriaRole.Link, new() { Name = fileNameFragment }).ClickAsync();
        await page.WaitForURLAsync("**/data-import/sessions/*", new() { Timeout = 15_000 });
    }
}
