using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the document type create/edit page.
/// Routes: /companies/{id}/document-types/new  and  /companies/{id}/document-types/{id}
/// </summary>
public sealed class DocumentTypeEditPage(IPage page, string baseUrl)
{
    public async Task GoToNewAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/document-types/new");
        await page.WaitForSelectorAsync("button:has-text('Save')", new() { Timeout = 20_000 });
    }

    public async Task FillNameAsync(string name)
    {
        await page.GetByPlaceholder("e.g. Passport, Contract").FillAsync(name);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillDescriptionAsync(string description)
    {
        await page.GetByPlaceholder("Optional description").FillAsync(description);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task SaveAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await page.WaitForURLAsync("**/document-types", new() { Timeout = 15_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public async Task<bool> HasErrorAsync()
    {
        try
        {
            await page.Locator(".alert-danger, .validation-message").First.WaitForAsync(new() { Timeout = 5_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task<string> GetNameAsync() =>
        await page.GetByPlaceholder("e.g. Passport, Contract").InputValueAsync();

    /// <summary>Navigates straight to an existing document type's edit page (/companies/{id}/document-types/{id}).</summary>
    public async Task GoToEditAsync(Guid companyId, Guid id)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/document-types/{id}");
        await page.WaitForSelectorAsync("button:has-text('Save')", new() { Timeout = 20_000 });
    }

    // ── Description field (optional HrTextBox / SfTextBox) ─────────────────────
    // Same click-focus / select-all / delete / type-for-real / Tab-to-commit technique as
    // CompanyEditPage.SetFirstAddressLine1Async — a bare FillAsync sets the DOM value through CDP
    // and bypasses the Syncfusion component's own keyup/input listeners, so the typed value never
    // round-trips to the Blazor-bound model.
    public async Task SetDescriptionAsync(string value)
    {
        var input = page.GetByPlaceholder("Optional description");
        await input.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.PressAsync("Delete");
        await page.WaitForTimeoutAsync(150);
        if (value.Length > 0)
            await input.PressSequentiallyAsync(value, new() { Delay = 30 });
        await page.Keyboard.PressAsync("Tab");
        await page.WaitForTimeoutAsync(300);
    }

    public Task<string> GetDescriptionAsync() =>
        page.GetByPlaceholder("Optional description").InputValueAsync();

    /// <summary>Auto-retrying wait for the Description field to hold <paramref name="expected"/> (used after an async reload/round-trip).</summary>
    public async Task<string> WaitForDescriptionAsync(string expected)
    {
        var input = page.GetByPlaceholder("Optional description");
        await Assertions.Expect(input).ToHaveValueAsync(expected, new() { Timeout = 15_000 });
        return await input.InputValueAsync();
    }

    // ── Optimistic-concurrency conflict banner (Ticket 2) ─────────────────────
    // DocumentTypeEdit.razor renders the shared <SaveConflictBanner> via EditPageBase — a single
    // `div.alert.alert-warning.save-conflict-banner[role='alert']` with a "Reload latest values"
    // Syncfusion button. Scope on the component's own `.save-conflict-banner` class (+ role='alert')
    // and additionally require the "Reload latest values" action so an unrelated warning alert can
    // never satisfy strict mode. Match on structure, not text.
    private ILocator ConcurrencyWarningBanner =>
        page.Locator(".save-conflict-banner[role='alert']")
            .Filter(new() { Has = page.GetByRole(AriaRole.Button, new() { Name = "Reload latest values" }) });

    /// <summary>
    /// Clicks Save and waits for the optimistic-concurrency banner to appear — i.e. the save was
    /// rejected (HTTP 409) because the document type changed since this form loaded it. A conflicted
    /// save stays on the edit page (no navigation to the list).
    /// </summary>
    public async Task SaveExpectingConflictAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await ConcurrencyWarningBanner.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 20_000 });
    }

    public Task<bool> IsConcurrencyWarningVisibleAsync() =>
        ConcurrencyWarningBanner.IsVisibleAsync();

    /// <summary>Clicks "Reload latest values" in the concurrency banner and waits for it to clear.</summary>
    public async Task ClickReloadLatestValuesAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Reload latest values" }).ClickAsync();
        await ConcurrencyWarningBanner.WaitForAsync(
            new() { State = WaitForSelectorState.Hidden, Timeout = 20_000 });
        // The reload round-trips the service Get before repopulating the model; give the re-bound
        // values a beat to land before callers read them.
        await page.WaitForTimeoutAsync(300);
    }

    private ILocator UnsavedChangesDialog => page.Locator("[role='dialog']:has-text('Unsaved Changes')");

    public Task ClickCloseAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true }).ClickAsync();

    public Task<bool> IsUnsavedChangesDialogVisibleAsync() =>
        UnsavedChangesDialog.WaitUntilVisibleAsync();

    public async Task ConfirmDiscardChangesAsync()
    {
        await UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Discard Changes" }).ClickAsync();
        await page.WaitForURLAsync("**/document-types", new() { Timeout = 15_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public async Task ConfirmSaveFromUnsavedChangesDialogAsync()
    {
        await UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await page.WaitForURLAsync("**/document-types", new() { Timeout = 15_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public Task CancelUnsavedChangesDialogAsync() =>
        UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();

    public async Task CloseAndWaitForListAsync()
    {
        await ClickCloseAsync();
        await page.WaitForURLAsync("**/document-types", new() { Timeout = 15_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }
}
