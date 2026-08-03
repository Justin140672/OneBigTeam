using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the asset create/edit page.
/// Routes: /companies/{id}/assets/new  and  /companies/{id}/assets/{id}
/// </summary>
public sealed class AssetEditPage(IPage page, string baseUrl)
{
    public async Task GoToNewAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/assets/new");
        // AssetEdit has an SfDropDownList for Category; span[role='combobox'] only appears
        // after Blazor's interactive render, ensuring event handlers are wired up.
        await page.WaitForSelectorAsync("span[role='combobox']", new() { Timeout = 20_000 });
    }

    public async Task FillAssetNumberAsync(string value) =>
        await page.GetByPlaceholder("e.g. ASSET-001").FillAsync(value);

    public async Task FillNameAsync(string value) =>
        await page.GetByPlaceholder("e.g. Dell Laptop").FillAsync(value);

    /// <summary>
    /// Selects a category from the Category dropdown (no filtering support on this field). Scoped
    /// to the field's own ".col-md-6" group — an unscoped "body" locator would match the *first*
    /// span[role='combobox'] anywhere on the page in DOM order, which is the top navbar's user
    /// account dropdown, not this field.
    /// </summary>
    public Task SelectCategoryAsync(string categoryName) =>
        DropDownSelector.SelectAsync(page, page.Locator(".col-md-6").Filter(new() { HasText = "Category" }).First, categoryName);

    public async Task SaveAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await page.WaitForURLAsync("**/assets", new() { Timeout = 15_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public async Task<bool> HasErrorAsync() =>
        await page.Locator(".alert-danger, .validation-message").First.IsVisibleAsync();

    public async Task<string> GetNameAsync() =>
        await page.GetByPlaceholder("e.g. Dell Laptop").InputValueAsync();

    private ILocator UnsavedChangesDialog => page.Locator("[role='dialog']:has-text('Unsaved Changes')");

    public Task ClickCloseAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true }).ClickAsync();

    public Task<bool> IsUnsavedChangesDialogVisibleAsync() =>
        UnsavedChangesDialog.IsVisibleAsync();

    public async Task ConfirmDiscardChangesAsync()
    {
        await UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Discard Changes" }).ClickAsync();
        await page.WaitForURLAsync("**/assets", new() { Timeout = 15_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public async Task ConfirmSaveFromUnsavedChangesDialogAsync()
    {
        await UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await page.WaitForURLAsync("**/assets", new() { Timeout = 15_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public Task CancelUnsavedChangesDialogAsync() =>
        UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();

    public async Task CloseAndWaitForListAsync()
    {
        await ClickCloseAsync();
        await page.WaitForURLAsync("**/assets", new() { Timeout = 15_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }
}
