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

    public async Task<string> GetTitleAsync() =>
        await page.GetByPlaceholder("e.g. Senior Software Engineer").InputValueAsync();

    public async Task FillProbationMonthsOverrideAsync(int months) =>
        await page.GetByPlaceholder("Use company default").FillAsync(months.ToString());

    public async Task FillSalaryRangeAsync(decimal min, decimal max)
    {
        await page.GetByPlaceholder("Min").FillAsync(min.ToString());
        await page.GetByPlaceholder("Max").FillAsync(max.ToString());
    }

    public async Task SetUseCompanyWorkingPatternAsync(bool useCompanyDefault)
    {
        var checkbox = page.GetByLabel("Use company working pattern");
        var isChecked = await checkbox.IsCheckedAsync();
        if (useCompanyDefault && !isChecked) await checkbox.CheckAsync();
        if (!useCompanyDefault && isChecked) await checkbox.UncheckAsync();
    }

    public async Task SelectDefaultLeavePolicyAsync(string leavePolicyName)
    {
        // The Defaults card now has three comboboxes (Salary Type, Default Leave Policy,
        // Onboarding Template), so scope to the specific field wrapper by its label rather
        // than taking .First within the whole card.
        var field = page.Locator(".mb-3", new PageLocatorOptions { HasText = "Default Leave Policy" });
        await field.Locator("span[role='combobox']").First.ClickAsync();
        await page.Locator(".e-popup-open .e-list-item")
            .Filter(new() { HasText = leavePolicyName })
            .First
            .ClickAsync(new() { Timeout = 10_000 });
    }

    private ILocator UnsavedChangesDialog => page.Locator("[role='dialog']:has-text('Unsaved Changes')");

    public Task ClickCloseAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true }).ClickAsync();

    public Task<bool> IsUnsavedChangesDialogVisibleAsync() =>
        UnsavedChangesDialog.IsVisibleAsync();

    public async Task ConfirmDiscardChangesAsync()
    {
        await UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Discard Changes" }).ClickAsync();
        await page.WaitForURLAsync("**/position-profiles", new() { Timeout = 15_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public async Task ConfirmSaveFromUnsavedChangesDialogAsync()
    {
        await UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await page.WaitForURLAsync("**/position-profiles", new() { Timeout = 15_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public Task CancelUnsavedChangesDialogAsync() =>
        UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();

    public async Task CloseAndWaitForListAsync()
    {
        await ClickCloseAsync();
        await page.WaitForURLAsync("**/position-profiles", new() { Timeout = 15_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

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

    // Waiting for the Add button/empty-state text (OpenRequiredDocumentsTabAsync) only proves the
    // Blazor component has mounted, not that Syncfusion's EJ2 grid has finished its own JS render
    // pass to populate ".e-row" — wait for the row selector itself (or the empty-state text) here
    // too, since these methods are also called after adding/removing a row, which re-fetches.
    private const string RequiredDocumentsRowsRenderedSelector =
        ".e-grid .e-row, .text-muted:has-text('No required documents')";

    public async Task<bool> HasRequiredDocumentInGridAsync(string documentTypeName)
    {
        await page.WaitForSelectorAsync(RequiredDocumentsRowsRenderedSelector, new() { Timeout = 15_000 });

        var rows = page.Locator(".e-grid .e-row").Filter(new() { HasText = documentTypeName });
        return await rows.CountAsync() > 0;
    }

    public async Task ClickRemoveRequiredDocumentAsync(string documentTypeName)
    {
        await page.WaitForSelectorAsync(RequiredDocumentsRowsRenderedSelector, new() { Timeout = 15_000 });

        var row = page.Locator(".e-grid .e-row").Filter(new() { HasText = documentTypeName }).First;
        await row.GetByTitle("Remove").ClickAsync();
    }

    public async Task ConfirmRemoveAsync() =>
        await page.GetByRole(AriaRole.Button, new() { Name = "Yes" }).ClickAsync();
}
