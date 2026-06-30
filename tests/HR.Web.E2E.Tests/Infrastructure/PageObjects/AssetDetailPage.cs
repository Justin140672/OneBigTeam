using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class AssetDetailPage(IPage page, string baseUrl)
{
    public async Task GoToAsync(Guid companyId, Guid assetId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/assets/{assetId}/view");
        await page.WaitForSelectorAsync("[data-testid='asset-detail-page']", new() { Timeout = 20_000 });
        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border')",
            null, new PageWaitForFunctionOptions { Timeout = 20_000 });
    }

    public async Task<string> GetAssetNumberAsync() =>
        (await page.Locator("[data-testid='asset-number']").TextContentAsync())?.Trim() ?? "";

    public async Task<string> GetAssetNameAsync() =>
        (await page.Locator("[data-testid='asset-name']").TextContentAsync())?.Trim() ?? "";

    public async Task<string> GetCategoryAsync() =>
        (await page.Locator("[data-testid='asset-category']").TextContentAsync())?.Trim() ?? "";

    public async Task<string> GetStatusAsync() =>
        (await page.Locator("[data-testid='asset-status']").First.TextContentAsync())?.Trim() ?? "";

    public async Task<string> GetManufacturerAsync() =>
        (await page.Locator("[data-testid='asset-manufacturer']").TextContentAsync())?.Trim() ?? "";

    public async Task<string> GetModelAsync() =>
        (await page.Locator("[data-testid='asset-model']").TextContentAsync())?.Trim() ?? "";

    public async Task<string> GetSerialNumberAsync() =>
        (await page.Locator("[data-testid='asset-serial']").TextContentAsync())?.Trim() ?? "";

    public async Task<string> GetTitleAsync() =>
        (await page.Locator("[data-testid='asset-title']").TextContentAsync())?.Trim() ?? "";

    public async Task<bool> IsNotFoundAlertVisibleAsync() =>
        await page.Locator(".alert-danger").IsVisibleAsync();
}
