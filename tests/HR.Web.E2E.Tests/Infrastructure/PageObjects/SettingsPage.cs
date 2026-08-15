using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for HR.Admin.Web's Settings.razor (/settings) — the Platform Settings page, the
/// final Admin Portal backlog item. It's a single-panel form (no Syncfusion dropdowns/comboboxes,
/// so DropDownSelector is not used here): trial length + default monthly price
/// (SfNumericTextBox), support email (SfTextBox), maintenance mode (SfCheckBox, conditionally
/// revealing a maintenance message SfTextBox), and a dynamic list of feature flag rows (name
/// SfTextBox + enabled SfCheckBox + Remove SfButton, plus an "+ Add flag" button). Save goes
/// through the shared AdminActionConfirmDialog (mandatory reason, min 5 chars — see
/// AdminActionConfirmDialog.razor), titled "Save platform settings" with confirm button text
/// "Save changes" — see Settings.razor's AdminActionConfirmDialog usage.
///
/// On successful save, Settings.razor's OnConfirmedAsync sets _settings/_saveSucceeded and then
/// calls LoadAsync() again (a fresh GET), so the form re-populates from the server response
/// in-place — there is no separate page navigation/reload required by tests.
/// </summary>
public sealed class SettingsPage(IPage page, string baseUrl)
{
    // Settings.razor always renders exactly one of: the loading text, the "not authorised"
    // dashboard-error div, or the settings form itself (identified by the trial-length field) —
    // wait for any "settled" state.
    private const string SettledSelector = ".dashboard-error, #trial-length-days";

    public async Task GoToAsync()
    {
        await page.GotoAsync($"{baseUrl}/settings");
        await page.WaitForSelectorAsync(SettledSelector, new() { Timeout = 20_000 });
    }

    public Task<bool> IsErrorBannerVisibleAsync() =>
        page.Locator(".dashboard-error").IsVisibleAsync();

    public Task<bool> IsFormVisibleAsync() =>
        page.Locator("#trial-length-days").IsVisibleAsync();

    // --- General fields ---

    // Syncfusion's SfNumericTextBox puts the HtmlAttributes id directly on the rendered <input>
    // itself (same convention documented in EmployeeAdminPage.cs for e-numerictextbox), unlike
    // e.g. a wrapper-based component — so no nested "input" descendant selector is needed here.
    private ILocator TrialLengthInput => page.Locator("#trial-length-days");

    private ILocator DefaultMonthlyPriceInput => page.Locator("#default-monthly-price");

    private ILocator SupportEmailInput => page.Locator("#support-email");

    public async Task<string> GetTrialLengthAsync() => await TrialLengthInput.InputValueAsync();

    public async Task<string> GetDefaultMonthlyPriceAsync() => await DefaultMonthlyPriceInput.InputValueAsync();

    public async Task<string> GetSupportEmailAsync() => await SupportEmailInput.InputValueAsync();

    /// <summary>
    /// Fills the trial length numeric field. SfNumericTextBox's server-side bound value only
    /// round-trips over the Blazor Server circuit on blur/change, not on FillAsync's raw "input"
    /// DOM event alone — same convention documented on AdminLoginPage.LoginAsync — so a Tab
    /// follows every fill in this page object.
    /// </summary>
    public async Task SetTrialLengthAsync(string value)
    {
        await TrialLengthInput.FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task SetDefaultMonthlyPriceAsync(string value)
    {
        await DefaultMonthlyPriceInput.FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task SetSupportEmailAsync(string value)
    {
        await SupportEmailInput.FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    // --- Maintenance mode ---

    private ILocator MaintenanceModeCheckbox =>
        page.Locator(".settings-checkbox-field .e-checkbox-wrapper, .settings-checkbox-field input[type='checkbox']").First;

    private ILocator MaintenanceMessageInput => page.Locator("#maintenance-message");

    public Task<bool> IsMaintenanceModeCheckedAsync() =>
        page.Locator(".settings-checkbox-field input[type='checkbox']").First.IsCheckedAsync();

    public async Task ToggleMaintenanceModeAsync()
    {
        await MaintenanceModeCheckbox.ClickAsync();
    }

    public Task<bool> IsMaintenanceMessageVisibleAsync() => MaintenanceMessageInput.IsVisibleAsync();

    public async Task SetMaintenanceMessageAsync(string value)
    {
        await MaintenanceMessageInput.FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public Task<string> GetMaintenanceMessageAsync() => MaintenanceMessageInput.InputValueAsync();

    // --- Feature flags ---

    private ILocator FeatureFlagRows => page.Locator(".settings-flag-row");

    public Task<int> GetFeatureFlagRowCountAsync() => FeatureFlagRows.CountAsync();

    public Task ClickAddFlagAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "+ Add flag" }).ClickAsync();

    public async Task SetFlagNameAsync(int index, string name)
    {
        var input = FeatureFlagRows.Nth(index).Locator(".settings-flag-name input, input.settings-flag-name");
        await input.FillAsync(name);
        await page.Keyboard.PressAsync("Tab");
    }

    public Task<bool> IsFlagEnabledAsync(int index) =>
        FeatureFlagRows.Nth(index).Locator("input[type='checkbox']").IsCheckedAsync();

    public async Task ToggleFlagEnabledAsync(int index)
    {
        // Click the checkbox's wrapper (Syncfusion SfCheckBox), same convention as
        // MaintenanceModeCheckbox above — clicking the raw input directly can be intercepted by
        // the wrapper's overlay.
        await FeatureFlagRows.Nth(index).Locator(".e-checkbox-wrapper").ClickAsync();
    }

    public Task RemoveFlagAsync(int index) =>
        FeatureFlagRows.Nth(index).GetByRole(AriaRole.Button, new() { Name = "Remove" }).ClickAsync();

    /// <summary>Finds a feature flag row's index by its current name value, or -1 if not found.</summary>
    public async Task<int> FindFlagRowIndexAsync(string name)
    {
        var count = await GetFeatureFlagRowCountAsync();
        for (var i = 0; i < count; i++)
        {
            var input = FeatureFlagRows.Nth(i).Locator(".settings-flag-name input, input.settings-flag-name");
            if (await input.InputValueAsync() == name)
                return i;
        }

        return -1;
    }

    // --- Last updated (read-only) ---

    private ILocator LastUpdatedSection =>
        page.Locator(".details-panel").Filter(new() { HasText = "Last updated" });

    public Task<string?> GetLastUpdatedWhenTextAsync() =>
        LastUpdatedSection.Locator("dd").First.TextContentAsync();

    // --- Save action / feedback ---

    public Task ClickSaveAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "Save changes", Exact = true }).ClickAsync();

    public ILocator SaveDialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Save platform settings" });

    public async Task FillDialogReasonAsync(string reason)
    {
        await SaveDialog.Locator("#admin-action-reason").FillAsync(reason);
        await page.Keyboard.PressAsync("Tab");
    }

    public Task ClickDialogConfirmAsync() =>
        SaveDialog.GetByRole(AriaRole.Button, new() { Name = "Save changes", Exact = true }).ClickAsync();

    /// <summary>
    /// Full save flow: opens the confirm dialog, fills a valid reason, and confirms. Does not
    /// wait for the resulting success/error banner — callers assert on that separately.
    /// </summary>
    public async Task SaveAsync(string reason = "E2E: updating platform settings")
    {
        await ClickSaveAsync();
        await SaveDialog.WaitForAsync(new() { Timeout = 10_000 });
        await FillDialogReasonAsync(reason);
        await ClickDialogConfirmAsync();
    }

    public Task<bool> IsSuccessBannerVisibleAsync() =>
        page.Locator(".admin-action-success").IsVisibleAsync();

    public Task<bool> IsErrorListVisibleAsync() =>
        page.Locator("ul.admin-action-error").IsVisibleAsync();

    public Task<string?> GetErrorListTextAsync() =>
        page.Locator("ul.admin-action-error").TextContentAsync();
}
