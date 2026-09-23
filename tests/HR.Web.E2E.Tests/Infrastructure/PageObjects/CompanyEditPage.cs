using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the company edit page (/companies/{id}/edit). A single Profile tab now covers
/// company name, status, and addresses — the former Addresses tab was merged into it and the
/// Settings tab (Regional TimeZone/Locale + Backfill Employee Timeline trigger) was removed
/// outright (UK-only customers for now; revisited via the Admin app if that changes). Branding
/// still exists as a component but is no longer rendered as its own tab.
/// </summary>
public sealed class CompanyEditPage(IPage page, string baseUrl)
{
    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/edit");
        await page.WaitForSelectorAsync("#company-name", new() { Timeout = 20_000 });
    }

    // ── Tab navigation ─────────────────────────────────────────────────────────

    /// <summary>
    /// The Profile tab is now the only tab (Addresses merged in, Settings removed, Branding
    /// hidden) — there is no longer a tablist/tab to click. This is a no-op kept so existing
    /// call sites don't need to change; it just waits for the profile card to be ready.
    /// </summary>
    public async Task OpenProfileTabAsync()
    {
        await page.WaitForSelectorAsync(".card", new() { Timeout = 15_000 });
    }

    // ── Profile tab ────────────────────────────────────────────────────────────

    /// <summary>Returns the company name shown in the h1 heading.</summary>
    public async Task<string> GetCompanyNameAsync() =>
        (await page.Locator("h1").TextContentAsync())?.Trim() ?? "";

    // ── Save ───────────────────────────────────────────────────────────────────

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

    /// <summary>Clicks "Reload latest values" in the concurrency banner and waits for it to clear.</summary>
    public async Task ClickReloadLatestValuesAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Reload latest values" }).ClickAsync();
        await ConcurrencyWarningBanner.WaitForAsync(
            new() { State = WaitForSelectorState.Hidden, Timeout = 20_000 });
        // The reload round-trips CompanyService.GetCompanyAsync before repopulating the model;
        // give the re-bound values a beat to land before callers read them.
        await page.WaitForTimeoutAsync(300);
    }

    /// <summary>Fills the company Name field on the Profile tab.</summary>
    public async Task FillCompanyNameInputAsync(string value)
    {
        await page.GetByPlaceholder("Company name").FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task<string> GetCompanyNameInputValueAsync() =>
        await page.GetByPlaceholder("Company name").InputValueAsync();

    // ── Addresses (merged into the Profile tab) ───────────────────────────────

    /// <summary>The first address block's "Line 1" field — Acme has more than one address type
    /// (Registered Office, Trading Address) seeded, so this always targets the first.</summary>
    private ILocator FirstAddressLine1Input => page.GetByPlaceholder("Line 1").First;

    // FillAsync sets a Syncfusion SfTextBox's DOM value through CDP directly, which bypasses the
    // component's own JS keyup/input listeners that sync the typed value back to the Blazor-bound
    // model — a value that visually "fills" never actually round-trips to the server. Click-to-
    // focus, select-all, delete, then type each character for real, then Tab to blur/commit —
    // same technique as the old Settings tab's TypeIntoTextBoxAsync (removed along with that tab).
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

    // ── Close / unsaved-changes prompt (EditPageBase) ──────────────────────────

    private ILocator UnsavedChangesDialog => page.Locator("[role='dialog']:has-text('Unsaved Changes')");

    public Task ClickCloseAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true }).ClickAsync();

    public Task<bool> IsUnsavedChangesDialogVisibleAsync() =>
        UnsavedChangesDialog.WaitUntilVisibleAsync();

    /// <summary>
    /// Close navigates to "/" (CompanyEdit has no dedicated list page) — but Home.razor's
    /// role-based landing redirect immediately bounces a CompanyAdministrator-only user (who has
    /// no HR/Recruitment/Manager dashboard) straight back to this same Company edit page, so
    /// that's the URL that actually settles. See AppSession.LandingUrl.
    /// </summary>
    public Task CloseAndWaitForDashboardAsync(string baseUrl, Guid companyId) =>
        ClickAndWaitForRoundTripBackToEditAsync(ClickCloseAsync, baseUrl, companyId);

    public Task ConfirmDiscardChangesAsync(string baseUrl, Guid companyId) =>
        ClickAndWaitForRoundTripBackToEditAsync(
            () => UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Discard Changes" }).ClickAsync(),
            baseUrl, companyId);

    /// <summary>
    /// Choosing "Save" from the unsaved-changes prompt always navigates away on success — unlike
    /// the page's own Save button, which stays put and shows an inline success banner instead.
    /// </summary>
    public Task ConfirmSaveFromUnsavedChangesDialogAsync(string baseUrl, Guid companyId) =>
        ClickAndWaitForRoundTripBackToEditAsync(
            () => UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync(),
            baseUrl, companyId);

    /// <summary>
    /// Close/Discard/Save-and-close all end in EditPageBase.NavigateToList(), which does a
    /// forceLoad (full document) navigation to "/" (CompanyEdit's ListUrl — it has no list page);
    /// Home.razor then client-side-redirects a CompanyAdministrator straight back to this same
    /// /companies/{id}/edit URL. Because the final URL equals the starting URL, waiting for it
    /// matches instantly — before the "/" document navigation has even started — and the caller's
    /// next GotoAsync then collides with that in-flight navigation (net::ERR_ABORTED) or reads
    /// pre-save data. Instead, start listening for the "/" document navigation BEFORE clicking
    /// (so it can't be missed), then wait for the redirect back and for the page to render.
    /// </summary>
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
