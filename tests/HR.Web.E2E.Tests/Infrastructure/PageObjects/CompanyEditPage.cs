using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the company edit page (/companies/{id}/edit).
/// Covers the Profile and Settings tabs.
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

    public async Task OpenSettingsTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Settings" }).ClickAsync();
        await page.WaitForSelectorAsync(".card", new() { Timeout = 15_000 });
        // Wait for Blazor's interactive render to finish populating the tab. The tab was slimmed
        // down to just the Regional (TimeZone/Locale) fields and the Backfill Employee Timeline
        // button — it no longer has any Syncfusion dropdown, so wait on the Time Zone input
        // instead of the old span[role='combobox'] check (Leave Year Start Month has moved to
        // the standalone HR Settings page — see HrSettingsPage).
        await page.WaitForSelectorAsync("input[placeholder='Time Zone']", new() { Timeout = 20_000 });
    }

    public async Task OpenAddressesTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Addresses" }).ClickAsync();
        await page.WaitForSelectorAsync(".card", new() { Timeout = 15_000 });
    }

    // ── Profile tab ────────────────────────────────────────────────────────────

    /// <summary>Returns the company name shown in the h1 heading.</summary>
    public async Task<string> GetCompanyNameAsync() =>
        (await page.Locator("h1").TextContentAsync())?.Trim() ?? "";

    public async Task<bool> IsActiveAsync() =>
        await page.Locator("h1 ~ .badge.bg-success, .badge.bg-success").First.IsVisibleAsync();

    // ── Settings tab ───────────────────────────────────────────────────────────

    /// <summary>
    /// The "Time Zone" HrTextBox in the Regional section — a plain HTML placeholder attribute
    /// (HrTextBox uses FloatLabelType.Never by default), same rendering as the "Locale" field.
    /// </summary>
    private ILocator TimeZoneInput => page.GetByPlaceholder("Time Zone");

    // FillAsync sets a Syncfusion SfTextBox's DOM value through CDP directly, which bypasses the
    // component's own JS keyup/input listeners that sync the typed value back to the Blazor-bound
    // model — so a value that visually "fills" never actually round-trips to the server (see
    // EmployeeEditPage.TypeIntoNumericInputAsync for the same issue on SfNumericTextBox). Click-to-
    // focus, select-all, delete, then type each character for real, then Tab to blur/commit.
    private async Task TypeIntoTextBoxAsync(ILocator input, string value)
    {
        await input.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.PressAsync("Delete");
        // Give the clear a moment to actually land before typing — same mitigation applied to
        // EmployeeEditPage.TypeIntoNumericInputAsync for the same observed corruption (old value
        // still present, new text appended rather than replacing it).
        await page.WaitForTimeoutAsync(150);
        if (value.Length > 0)
            await input.PressSequentiallyAsync(value, new() { Delay = 30 });
        await page.Keyboard.PressAsync("Tab");
        // Tab blurs the Syncfusion SfTextBox, which raises its change event and round-trips the
        // new value to the server over the Blazor Server circuit — that round-trip is async and
        // not complete the instant PressAsync("Tab") returns. A caller that immediately clicks
        // Close (as Close_SaveFromUnsavedChangesDialog_PersistsAndNavigatesAway does) can open the
        // "unsaved changes" dialog and choose Save before Model.TimeZone/Model.Locale have
        // actually been updated server-side, silently saving the pre-edit value (observed:
        // "UTC-edited" typed and blurred, but "UTC" persisted).
        //
        // NOTE: an earlier fix here waited on Assertions.Expect(input).ToHaveValueAsync(value) as
        // a proxy for the round-trip, but that assertion is trivially satisfied the instant
        // PressSequentiallyAsync finishes typing — real keystrokes already update the input's DOM
        // value synchronously, well before Tab is even pressed. It does not observe the blur ->
        // change event -> SignalR round-trip -> Model update at all, so the race it was meant to
        // close was never actually closed. There's no DOM-observable signal that the server-side
        // bound property has been updated, so give the round-trip a fixed settling window instead.
        await page.WaitForTimeoutAsync(300);
    }

    public Task SetTimeZoneAsync(string value) => TypeIntoTextBoxAsync(TimeZoneInput, value);

    public Task<string> GetTimeZoneAsync() => TimeZoneInput.InputValueAsync();

    /// <summary>The "Locale" HrTextBox in the Regional section.</summary>
    private ILocator LocaleInput => page.GetByPlaceholder("Locale");

    public Task SetLocaleAsync(string value) => TypeIntoTextBoxAsync(LocaleInput, value);

    public Task<string> GetLocaleAsync() => LocaleInput.InputValueAsync();

    /// <summary>
    /// The "Backfill Employee Timeline…" button in the "Employee Timeline" subsection. Only rendered
    /// (@if in CompanySettingsTab.razor) while Session.CanManageEmployees is true (mirrors the
    /// server-side "employee:manage" policy) — note this is a *different* gate than the
    /// Session.CanManageCompany gate on the Settings tab itself.
    /// </summary>
    private ILocator BackfillEmployeeTimelineButton =>
        page.GetByRole(AriaRole.Button, new() { Name = "Backfill Employee Timeline…" });

    public Task<bool> IsBackfillEmployeeTimelineButtonVisibleAsync() =>
        BackfillEmployeeTimelineButton.IsVisibleAsync();

    public Task OpenBackfillEmployeeTimelineDialogAsync() =>
        BackfillEmployeeTimelineButton.ClickAsync();

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
