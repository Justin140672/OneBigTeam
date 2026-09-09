using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the Contact Details tab on the self-service My Profile page.
/// Selectors derived from MyProfileContactDetailsTab.razor (.cd-* CSS classes).
/// </summary>
public sealed class ContactDetailsTab(IPage page)
{
    public async Task WaitForLoadAsync() =>
        await page.WaitForSelectorAsync(".cd-card", new() { Timeout = 15_000 });

    public async Task<bool> IsVisibleAsync() =>
        await page.Locator(".cd-card").IsVisibleAsync();

    // ── Field accessors ────────────────────────────────────────────────────────

    public async Task FillPersonalEmailAsync(string email)
    {
        var input = page.GetByPlaceholder("e.g. name@personal.com");
        await input.ClearAsync();
        await input.FillAsync(email);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillMobilePhoneAsync(string phone)
    {
        var input = page.GetByPlaceholder("e.g. 07700 900000");
        await input.ClearAsync();
        await input.FillAsync(phone);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillHomePhoneAsync(string phone)
    {
        var input = page.GetByPlaceholder("e.g. 01234 567890");
        await input.ClearAsync();
        await input.FillAsync(phone);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillAddressLine1Async(string value)
    {
        var input = page.GetByPlaceholder("Street address");
        await input.ClearAsync();
        await input.FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillCityAsync(string value)
    {
        var input = page.GetByPlaceholder("e.g. London");
        await input.ClearAsync();
        await input.FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillPostCodeAsync(string value)
    {
        var input = page.GetByPlaceholder("e.g. SW1A 1AA");
        await input.ClearAsync();
        await input.FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    // Country is not shown on the contact details form (UK-only for now — the model defaults to
    // "United Kingdom"), so there is no FillCountryAsync.

    // ── Actions ────────────────────────────────────────────────────────────────

    public async Task SaveChangesAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save Changes" }).ClickAsync();
        // Wait for the success banner to appear.
        await page.WaitForSelectorAsync(".cd-success-banner", new() { Timeout = 15_000 });
    }

    /// <summary>Clicks Save without waiting for success — for asserting a validation error instead.</summary>
    public async Task ClickSaveAsync() =>
        await page.GetByRole(AriaRole.Button, new() { Name = "Save Changes" }).ClickAsync();

    // ── Optimistic-concurrency conflict banner (Ticket 2) ─────────────────────
    // MyProfileContactDetailsTab.razor renders the shared <SaveConflictBanner> component: a single
    // `div.alert.alert-warning.save-conflict-banner[role='alert']` containing a "Reload latest
    // values" Syncfusion button, distinct from the generic red `.alert-danger` GlobalError alert.
    // Scope on the component's own `.save-conflict-banner` class (+ role='alert'), not the shared
    // Bootstrap `.alert-warning` alone, and additionally require the "Reload latest values" action
    // so no unrelated warning alert can satisfy strict mode. Match on structure, not text: on a
    // real 409 the razor overrides the component's default Message with its own copy.
    private ILocator ConcurrencyWarningBanner =>
        page.Locator(".save-conflict-banner[role='alert']")
            .Filter(new() { Has = page.GetByRole(AriaRole.Button, new() { Name = "Reload latest values" }) });

    /// <summary>
    /// Clicks Save and waits for the optimistic-concurrency warning banner to appear — i.e. the
    /// save was rejected because the record changed since it was loaded. The caller's entered
    /// values are intentionally left untouched in the form.
    /// </summary>
    public async Task ClickSaveExpectingConflictAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save Changes" }).ClickAsync();
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
    }

    /// <summary>
    /// Bug fix (b): clears the required "Address Line 1" field and blurs it so the EditContext
    /// re-validates and reports a message. EditSectionBase.OnValidationStateChanged then drops any
    /// standing concurrency banner. Waits for the field-level validation message to confirm the
    /// invalid state registered (this tab renders <c>.validation-message</c>, not <c>.is-invalid</c>).
    /// </summary>
    public async Task MakeFormInvalidAsync()
    {
        // A bare FillAsync("") on the Syncfusion-backed HrTextBox doesn't drive its interop, so the
        // EditContext never sees the field change and no validation fires. Clear it "for real" —
        // focus, select-all, Delete — then Tab to commit the blur, the same technique the fill
        // helpers in this codebase use for these inputs.
        var input = page.GetByPlaceholder("Street address");
        await input.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.PressAsync("Delete");
        await page.WaitForTimeoutAsync(150);
        await page.Keyboard.PressAsync("Tab");
        await page.WaitForTimeoutAsync(300);
        await page.Locator(".validation-message").First.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    public async Task<string?> GetMobilePhoneValueAsync()
    {
        var input = page.GetByPlaceholder("e.g. 07700 900000");
        return await input.IsVisibleAsync() ? (await input.InputValueAsync()).Trim() : null;
    }

    public async Task<bool> IsSuccessBannerVisibleAsync() =>
        await page.Locator(".cd-success-banner").IsVisibleAsync();

    public async Task<bool> HasValidationErrorAsync()
    {
        try
        {
            await page.Locator(".is-invalid").First.WaitForAsync(new() { Timeout = 5_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    /// <summary>
    /// This tab has no per-field ".is-invalid" styling (see <see cref="HasValidationErrorAsync"/>) —
    /// a failed client-side Validate() instead surfaces as the GlobalError banner
    /// (EditSectionBase sets it to "Please correct the highlighted fields above.").
    /// </summary>
    public async Task<bool> HasGlobalErrorAsync()
    {
        // Checking IsVisibleAsync() immediately after ClickSaveAsync races the save round-trip
        // that renders this banner — give it a short window to appear.
        try
        {
            await page.Locator(".alert-danger").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    /// <summary>Returns the current value of the Work Email field (readonly).</summary>
    public async Task<string?> GetWorkEmailAsync()
    {
        var input = page.Locator(".cd-readonly").First;
        return await input.IsVisibleAsync()
            ? (await input.InputValueAsync()).Trim()
            : null;
    }

    /// <summary>Returns the current value of the Personal Email input via the live DOM property.</summary>
    public async Task<string?> GetPersonalEmailAsync()
    {
        var input = page.GetByPlaceholder("e.g. name@personal.com");
        return await input.IsVisibleAsync()
            ? (await input.InputValueAsync()).Trim()
            : null;
    }
}
