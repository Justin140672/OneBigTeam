using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the public holiday create/edit page.
/// Routes: /companies/{id}/public-holidays/new  and  /companies/{id}/public-holidays/{id}
/// </summary>
public sealed class PublicHolidayEditPage(IPage page, string baseUrl)
{
    public async Task GoToNewAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/public-holidays/new");
        // PublicHolidayEdit has an SfDatePicker; .e-date-wrapper only appears after Blazor's
        // interactive render, ensuring event handlers are wired up.
        await page.WaitForSelectorAsync(".e-date-wrapper", new() { Timeout = 20_000 });
    }

    public async Task FillDateAsync(string ddMMyyyy)
    {
        var dateInput = page.Locator(".e-date-wrapper input.e-input").First;
        await dateInput.ClickAsync();
        await dateInput.FillAsync(ddMMyyyy);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillNameAsync(string name)
    {
        await page.GetByPlaceholder("e.g. Christmas Day").FillAsync(name);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillCountryCodeAsync(string code)
    {
        await page.GetByPlaceholder("e.g. GB").FillAsync(code);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task GoToEditAsync(Guid companyId, Guid id)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/public-holidays/{id}");
        await page.WaitForSelectorAsync(".e-date-wrapper", new() { Timeout = 20_000 });
    }

    /// <summary>Extracts the holiday GUID from the current /public-holidays/{id} edit URL.</summary>
    public Guid CurrentHolidayId()
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            page.Url, @"/public-holidays/(?<id>[0-9a-fA-F-]{36})");
        if (!match.Success)
            throw new InvalidOperationException($"Not on a public holiday edit URL: {page.Url}");
        return Guid.Parse(match.Groups["id"].Value);
    }

    public async Task SaveAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        // Navigates back to the public-holidays list on success.
        await page.WaitForURLAsync("**/public-holidays", new() { Timeout = 15_000 });
        // With prerender:false the circuit connects after navigation, wait for the grid.
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    // ── Optimistic-concurrency conflict banner (Ticket 2) ─────────────────────
    // PublicHolidayEdit.razor renders the shared <SaveConflictBanner> — a single
    // `div.alert.alert-warning.save-conflict-banner[role='alert']` with the razor's own
    // "Someone else changed this public holiday…" message and a "Reload latest values" Syncfusion
    // button. Scope on the component's own `.save-conflict-banner` class (+ role='alert') and
    // additionally require the "Reload latest values" action so an unrelated warning alert can
    // never satisfy strict mode. Match on structure, not text.
    private ILocator ConcurrencyWarningBanner =>
        page.Locator(".save-conflict-banner[role='alert']")
            .Filter(new() { Has = page.GetByRole(AriaRole.Button, new() { Name = "Reload latest values" }) });

    /// <summary>
    /// Clicks Save and waits for the optimistic-concurrency banner to appear — i.e. the save was
    /// rejected (HTTP 409) because the holiday changed since this form loaded it. A conflicted save
    /// stays on the edit page (no navigation to the list).
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
        await page.GetByPlaceholder("e.g. Christmas Day").InputValueAsync();

    private ILocator UnsavedChangesDialog => page.Locator("[role='dialog']:has-text('Unsaved Changes')");

    public Task ClickCloseAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true }).ClickAsync();

    public Task<bool> IsUnsavedChangesDialogVisibleAsync() =>
        UnsavedChangesDialog.WaitUntilVisibleAsync();

    public async Task ConfirmDiscardChangesAsync()
    {
        await UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Discard Changes" }).ClickAsync();
        await page.WaitForURLAsync("**/public-holidays", new() { Timeout = 15_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public async Task ConfirmSaveFromUnsavedChangesDialogAsync()
    {
        await UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await page.WaitForURLAsync("**/public-holidays", new() { Timeout = 15_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public Task CancelUnsavedChangesDialogAsync() =>
        UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();

    public async Task CloseAndWaitForListAsync()
    {
        await ClickCloseAsync();
        await page.WaitForURLAsync("**/public-holidays", new() { Timeout = 15_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }
}
