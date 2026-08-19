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
        await page.WaitForSelectorAsync("[role='tablist']", new() { Timeout = 20_000 });
    }

    // ── Tab navigation ─────────────────────────────────────────────────────────

    public async Task OpenProfileTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Profile" }).ClickAsync();
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
    public async Task CloseAndWaitForDashboardAsync(string baseUrl, Guid companyId)
    {
        await ClickCloseAsync();
        await page.WaitForURLAsync($"{baseUrl}/companies/{companyId}/edit", new() { Timeout = 15_000 });
    }

    public async Task ConfirmDiscardChangesAsync(string baseUrl, Guid companyId)
    {
        await UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Discard Changes" }).ClickAsync();
        // Close navigates to "/" first (CompanyEdit has no dedicated list page — see
        // CloseAndWaitForDashboardAsync's own remarks), which then immediately redirects a
        // CompanyAdministrator back to this same /companies/{id}/edit page via Home.razor's
        // role-based landing redirect (AppSession.LandingUrl). That's two navigations in
        // sequence, not one — under load the intermediate "/" landing can take longer to redirect
        // than this was originally budgeted for, observed as the caller reading _page.Url and
        // finding the bare "/" root instead of the final destination.
        await page.WaitForURLAsync($"{baseUrl}/companies/{companyId}/edit", new() { Timeout = 20_000 });
    }

    /// <summary>
    /// Choosing "Save" from the unsaved-changes prompt always navigates away on success — unlike
    /// the page's own Save button, which stays put and shows an inline success banner instead.
    /// </summary>
    public async Task ConfirmSaveFromUnsavedChangesDialogAsync(string baseUrl, Guid companyId)
    {
        // The "Save from unsaved changes" flow lands back on the SAME /edit URL it started from,
        // so WaitForURLAsync's pattern already matches the current URL before the click even
        // happens — it resolves instantly rather than actually waiting for the save round-trip to
        // complete, letting the caller re-navigate (GoToAsync) and read stale, unsaved data. Wait
        // for the confirmation dialog to actually close first, which only happens once the save
        // (and subsequent client-side navigation) has gone through.
        await UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await UnsavedChangesDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
        await page.WaitForURLAsync($"{baseUrl}/companies/{companyId}/edit", new() { Timeout = 15_000 });
    }

    public Task CancelUnsavedChangesDialogAsync() =>
        UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
}
