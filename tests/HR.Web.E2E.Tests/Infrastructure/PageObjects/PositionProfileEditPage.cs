using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the position profile create/edit page.
/// Routes: /companies/{id}/position-profiles/new  and  /companies/{id}/position-profiles/{id}
/// </summary>
public sealed class PositionProfileEditPage(IPage page, string baseUrl)
{
    public async Task GoToNewAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/position-profiles/new");
        // PositionProfileEdit has an SfDropDownList for Department; span[role='combobox'] only
        // appears after Blazor's interactive render, ensuring event handlers are wired up.
        await page.WaitForSelectorAsync("span[role='combobox']", new() { Timeout = 20_000 });
    }

    public async Task GoToAsync(Guid companyId, Guid positionProfileId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/position-profiles/{positionProfileId}");
        await page.WaitForSelectorAsync("span[role='combobox']", new() { Timeout = 20_000 });
    }

    public async Task FillTitleAsync(string title) =>
        await page.GetByPlaceholder("e.g. Senior Software Engineer").FillAsync(title);

    public async Task FillDescriptionAsync(string description) =>
        await page.GetByPlaceholder("Optional description").FillAsync(description);

    public async Task SetManagerialRoleAsync(bool isManagerial)
    {
        var checkbox = page.GetByLabel("Managerial role");
        var isChecked = await checkbox.IsCheckedAsync();
        if (isManagerial && !isChecked) await checkbox.CheckAsync();
        if (!isManagerial && isChecked) await checkbox.UncheckAsync();
    }

    public async Task SaveAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        // Navigates back to the position-profiles list on success.
        await page.WaitForURLAsync("**/position-profiles", new() { Timeout = 15_000 });
        // With prerender:false the circuit connects after navigation, wait for the grid.
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public async Task<bool> HasErrorAsync() =>
        await page.Locator(".alert-danger, .validation-message").First.IsVisibleAsync();

    public async Task OpenRequiredDocumentsTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Required Documents" }).ClickAsync();
        // Wait for the tab content to be interactive — either the Add button or the empty-state text.
        await page.WaitForSelectorAsync(
            "button:has-text('Add'), .text-muted:has-text('No required documents')",
            new() { Timeout = 15_000 });
    }

    public async Task<bool> HasRequiredDocumentsTabAsync() =>
        await page.GetByRole(AriaRole.Tab, new() { Name = "Required Documents" }).IsVisibleAsync();

    public async Task ClickAddRequiredDocumentAsync() =>
        await page.GetByRole(AriaRole.Button, new() { Name = "Add" }).ClickAsync();

    public async Task SelectDocumentTypeInDialogAsync(string documentTypeName)
    {
        // Wait for the SfDialog container — Syncfusion sets role="dialog" on the outer element.
        await page.Locator("[role='dialog']").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });

        // Syncfusion SfDropDownList puts role='combobox' on the outer SPAN wrapper, not the inner input.
        // Clicking the span opens the popup.
        await page.Locator("[role='dialog'] span[role='combobox']").First.ClickAsync();

        // Click the matching item in the open popup.
        await page.Locator(".e-popup-open .e-list-item")
            .Filter(new() { HasText = documentTypeName })
            .First
            .ClickAsync(new() { Timeout = 10_000 });
    }

    public async Task SubmitAddDialogAsync()
    {
        await page.Locator("[role='dialog'] .e-footer-content button:has-text('Add')").ClickAsync();
        await page.Locator("[role='dialog']").WaitForAsync(
            new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    public async Task<bool> HasRequiredDocumentInGridAsync(string documentTypeName)
    {
        var rows = page.Locator(".e-grid .e-row").Filter(new() { HasText = documentTypeName });
        return await rows.CountAsync() > 0;
    }

    public async Task ClickRemoveRequiredDocumentAsync(string documentTypeName)
    {
        var row = page.Locator(".e-grid .e-row").Filter(new() { HasText = documentTypeName }).First;
        await row.GetByTitle("Remove").ClickAsync();
    }

    public async Task ConfirmRemoveAsync() =>
        await page.GetByRole(AriaRole.Button, new() { Name = "Yes" }).ClickAsync();
}
