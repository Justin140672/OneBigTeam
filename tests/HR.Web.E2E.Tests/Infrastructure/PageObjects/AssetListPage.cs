using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the asset list page (/companies/{companyId}/assets).
/// </summary>
public sealed class AssetListPage(IPage page, string baseUrl)
{
    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/assets");
        await page.WaitForSelectorAsync(".e-grid, .spinner-border, .alert-danger",
            new() { Timeout = 20_000 });
        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    public async Task ClickNewAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Add" }).ClickAsync();
        await page.WaitForURLAsync("**/assets/new**", new() { Timeout = 15_000 });
    }

    public Task<bool> HasItemAsync(string nameFragment) =>
        page.Locator(".e-rowcell")
            .Filter(new() { HasText = nameFragment })
            .First
            .WaitUntilVisibleAsync();
}
