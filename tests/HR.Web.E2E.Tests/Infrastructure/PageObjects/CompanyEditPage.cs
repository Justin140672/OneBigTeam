using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class CompanyEditPage(IPage page, string baseUrl)
{
    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/edit");
        await page.WaitForSelectorAsync("#company-name", new() { Timeout = 20_000 });
    }


    public async Task OpenProfileTabAsync()
    {
        await page.WaitForSelectorAsync(".card", new() { Timeout = 15_000 });
    }


    public async Task<string> GetCompanyNameAsync() =>
        (await page.Locator("h1").TextContentAsync())?.Trim() ?? "";


    public async Task SaveAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await page.WaitForSpinnerToClearAsync();
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

    /// <summary>
    /// Clicks Save and waits for the inline "Company saved successfully." banner — CompanyEdit's
    /// own Save button intentionally stays on the page (no list to navigate to) and shows this
    /// <c>.alert-success</c> banner instead.
    /// </summary>
    public async Task SaveExpectingSuccessAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await page.WaitForSpinnerToClearAsync();
        await page.Locator(".alert-success").First.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 20_000 });
    }

    public Task<bool> IsSaveSuccessVisibleAsync() =>
        page.Locator(".alert-success").First.IsVisibleAsync();

    // ── Optimistic-concurrency conflict banner (Ticket 2) ─────────────────────
    // CompanyEdit.razor renders the shared <SaveConflictBanner> — a single
    // `div.alert.alert-warning.save-conflict-banner[role='alert']` carrying the razor's own
    // "Someone else changed this company…" message and a "Reload latest values" Syncfusion button.
    // Scope on the component's own `.save-conflict-banner` class (+ role='alert') rather than the
    // shared Bootstrap `.alert-warning`, and additionally require the "Reload latest values" action
    // so an unrelated warning alert can never satisfy strict mode. Match on structure, not text.
    private ILocator ConcurrencyWarningBanner =>
        page.Locator(".save-conflict-banner[role='alert']")
            .Filter(new() { Has = page.GetByRole(AriaRole.Button, new() { Name = "Reload latest values" }) });

    /// <summary>
    /// Clicks Save and waits for the optimistic-concurrency banner to appear — i.e. the save was
    /// rejected (HTTP 409) because the company changed since this form loaded it. Does not expect a
    /// navigation; a conflicted save stays on the edit page.
    /// </summary>
    public async Task SaveExpectingConflictAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await page.WaitForSpinnerToClearAsync();
        await ConcurrencyWarningBanner.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 20_000 });
    }

    public Task<bool> IsConcurrencyWarningVisibleAsync() =>
        ConcurrencyWarningBanner.IsVisibleAsync();

    public async Task ClickReloadLatestValuesAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Reload latest values" }).ClickAsync();
        await ConcurrencyWarningBanner.WaitForAsync(
            new() { State = WaitForSelectorState.Hidden, Timeout = 20_000 });
        await page.WaitForTimeoutAsync(300);
    }

    public async Task FillCompanyNameInputAsync(string value)
    {
        await page.GetByPlaceholder("Company name").FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task<string> GetCompanyNameInputValueAsync() =>
        await page.GetByPlaceholder("Company name").InputValueAsync();


    private ILocator FirstAddressLine1Input => page.GetByPlaceholder("Line 1").First;

    public async Task SetFirstAddressLine1Async(string value)
    {
        await FirstAddressLine1Input.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.PressAsync("Delete");
        await page.WaitForTimeoutAsync(150);
        if (value.Length > 0)
            await FirstAddressLine1Input.PressSequentiallyAsync(value, new() { Delay = 30 });
        await page.Keyboard.PressAsync("Tab");
        await page.WaitForTimeoutAsync(300);
    }

    public Task<bool> IsAddressLine1ValidationMessageVisibleAsync() =>
        page.Locator(".validation-message", new() { HasText = "Line 1 is required." }).First.IsVisibleAsync();

    public Task<string> GetFirstAddressLine1Async() => FirstAddressLine1Input.InputValueAsync();


    private ILocator UnsavedChangesDialog => page.Locator("[role='dialog']:has-text('Unsaved Changes')");

    public Task ClickCloseAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true }).ClickAsync();

    public Task<bool> IsUnsavedChangesDialogVisibleAsync() =>
        UnsavedChangesDialog.WaitUntilVisibleAsync();

    public Task CloseAndWaitForDashboardAsync(string baseUrl, Guid companyId) =>
        ClickAndWaitForRoundTripBackToEditAsync(ClickCloseAsync, baseUrl, companyId);

    public Task ConfirmDiscardChangesAsync(string baseUrl, Guid companyId) =>
        ClickAndWaitForRoundTripBackToEditAsync(
            () => UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Discard Changes" }).ClickAsync(),
            baseUrl, companyId);

    public Task ConfirmSaveFromUnsavedChangesDialogAsync(string baseUrl, Guid companyId) =>
        ClickAndWaitForRoundTripBackToEditAsync(
            () => UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync(),
            baseUrl, companyId);

    private async Task ClickAndWaitForRoundTripBackToEditAsync(Func<Task> click, string baseUrl, Guid companyId)
    {
        await page.RunAndWaitForNavigationAsync(click, new()
        {
            UrlFunc = url => new Uri(url).AbsolutePath == "/",
            WaitUntil = WaitUntilState.Commit,
            Timeout = 30_000,
        });
        await page.WaitForURLAsync($"{baseUrl}/companies/{companyId}/edit",
            new() { Timeout = 30_000, WaitUntil = WaitUntilState.Commit });
        await page.WaitForSelectorAsync("#company-name", new() { Timeout = 20_000 });
    }

    public Task CancelUnsavedChangesDialogAsync() =>
        UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
}
