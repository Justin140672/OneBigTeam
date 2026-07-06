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

    // ── Assignments section ───────────────────────────────────────────────────

    public async Task WaitForAssignmentsSectionAsync()
    {
        await page.WaitForSelectorAsync("[data-testid='asset-assignments-section']",
            new() { Timeout = 15_000 });
        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    public async Task<bool> IsAssignmentsSectionVisibleAsync() =>
        await page.Locator("[data-testid='asset-assignments-section']").IsVisibleAsync();

    public async Task<bool> HasAssignmentsGridRowsAsync() =>
        await page.Locator("[data-testid='asset-assignments-grid'] .e-row").CountAsync() > 0;

    public async Task<int> GetAssignmentsGridRowCountAsync() =>
        await page.Locator("[data-testid='asset-assignments-grid'] .e-row").CountAsync();

    public async Task<bool> IsAssignToEmployeeButtonVisibleAsync() =>
        await page.Locator("[data-testid='assign-to-employee-btn']").IsVisibleAsync();

    public async Task OpenAssignToEmployeeDialogAsync()
    {
        await page.Locator("[data-testid='assign-to-employee-btn']").ClickAsync();
        await page.WaitForSelectorAsync(".assign-to-employee-dialog", new() { Timeout = 10_000 });
    }

    public async Task<bool> IsAssignToEmployeeDialogVisibleAsync() =>
        await page.Locator("[role='dialog'].assign-to-employee-dialog").IsVisibleAsync();

    public async Task CloseAssignToEmployeeDialogAsync()
    {
        await page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
        await page.WaitForFunctionAsync(
            "!document.querySelector('.assign-to-employee-dialog') || !document.querySelector('.assign-to-employee-dialog').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
    }

    public async Task<bool> IsRequestReturnButtonVisibleAsync() =>
        await page.Locator("[data-testid='request-return-btn']").IsVisibleAsync();

    public async Task<bool> IsRequestReturnButtonDisabledAsync() =>
        await page.Locator("[data-testid='request-return-btn']").IsDisabledAsync();

    public async Task ClickRequestReturnAsync() =>
        await page.Locator("[data-testid='request-return-btn']").ClickAsync();

    public async Task<bool> IsAssignSuccessMessageVisibleAsync() =>
        await page.Locator("[data-testid='assign-success-message']").IsVisibleAsync();

    public async Task<string> GetAssignSuccessMessageAsync() =>
        (await page.Locator("[data-testid='assign-success-message']").TextContentAsync())?.Trim() ?? "";
}
