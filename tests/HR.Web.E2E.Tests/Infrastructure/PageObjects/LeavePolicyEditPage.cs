using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the leave policy create/edit page.
/// Routes: /companies/{id}/leave-policies/new  and  /companies/{id}/leave-policies/{id}
/// </summary>
public sealed class LeavePolicyEditPage(IPage page, string baseUrl)
{
    public async Task GoToNewAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/leave-policies/new");
        await page.WaitForSelectorAsync("button:has-text('Save')", new() { Timeout = 20_000 });
    }

    public async Task FillNameAsync(string name) =>
        await page.GetByPlaceholder("e.g. Standard Policy").FillAsync(name);

    public async Task FillDescriptionAsync(string description) =>
        await page.GetByPlaceholder("Optional description").FillAsync(description);

    public async Task SaveAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await page.WaitForURLAsync("**/leave-policies", new() { Timeout = 15_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public async Task<bool> HasErrorAsync() =>
        await page.Locator(".alert-danger, .validation-message").First.IsVisibleAsync();

    public async Task<string> GetNameAsync() =>
        await page.GetByPlaceholder("e.g. Standard Policy").InputValueAsync();

    private ILocator IsDefaultCheckbox => page.Locator("#isDefault");

    public async Task SetIsDefaultAsync(bool value)
    {
        var isChecked = await IsDefaultCheckbox.IsCheckedAsync();
        if (isChecked != value)
            await IsDefaultCheckbox.ClickAsync();
    }

    public Task<bool> IsDefaultCheckedAsync() => IsDefaultCheckbox.IsCheckedAsync();

    private ILocator UnsavedChangesDialog => page.Locator("[role='dialog']:has-text('Unsaved Changes')");

    public Task ClickCloseAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true }).ClickAsync();

    public Task<bool> IsUnsavedChangesDialogVisibleAsync() =>
        UnsavedChangesDialog.IsVisibleAsync();

    public async Task ConfirmDiscardChangesAsync()
    {
        await UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Discard Changes" }).ClickAsync();
        await page.WaitForURLAsync("**/leave-policies", new() { Timeout = 15_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public async Task ConfirmSaveFromUnsavedChangesDialogAsync()
    {
        await UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await page.WaitForURLAsync("**/leave-policies", new() { Timeout = 15_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public Task CancelUnsavedChangesDialogAsync() =>
        UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();

    public async Task CloseAndWaitForListAsync()
    {
        await ClickCloseAsync();
        await page.WaitForURLAsync("**/leave-policies", new() { Timeout = 15_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }
}
