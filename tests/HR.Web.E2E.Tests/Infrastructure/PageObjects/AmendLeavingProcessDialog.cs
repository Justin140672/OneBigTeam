using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the "Amend Leaving Process" dialog (AmendLeavingProcessDialog.razor), opened
/// via the "Amend" button in the Leaving tab's card header (only visible while the leaving
/// process's Status is "InProgress" — see <see cref="EmployeeLeavingTab.HasAmendButtonAsync"/>).
/// Unlike <see cref="StartLeavingProcessDialog"/> this is a single-step form (Leaving Date, Last
/// Working Day, Leaving Reason) that is pre-populated with the leaving process's current values
/// every time it's opened.
///
/// Follows the standalone-page-object-per-dialog pattern established by
/// <see cref="StartLeavingProcessDialog"/>.
/// </summary>
public sealed class AmendLeavingProcessDialog(IPage page)
{
    private ILocator Dialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Amend Leaving Process" });

    /// <summary>
    /// Clicks the Leaving tab's "Amend" button and waits for the dialog to open and finish
    /// fetching the leaving process's current values (AmendLeavingProcessDialog shows an
    /// HrLoadingIndicator, swapping the form fields in, once GetLeavingProcessAsync resolves).
    /// </summary>
    public async Task OpenAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Amend", Exact = true }).ClickAsync();
        await Dialog.WaitForAsync(new() { Timeout = 15_000 });
        await Dialog.Locator(".e-date-wrapper input.e-input").First
            .WaitForAsync(new() { Timeout = 15_000 });
    }

    public Task<bool> IsVisibleAsync() => Dialog.IsVisibleAsync();

    // ── Leaving Date ────────────────────────────────────────────────────────────

    private ILocator LeavingDateInput => Dialog.Locator(".e-date-wrapper input.e-input").Nth(0);

    /// <summary>
    /// Reads the current (pre-populated, or since-amended) Leaving Date value — used to assert
    /// the dialog opens pre-populated with the leaving process's existing value.
    /// </summary>
    public Task<string?> GetLeavingDateTextAsync() => LeavingDateInput.InputValueAsync();

    public async Task FillLeavingDateAsync(string ddMMyyyy)
    {
        await LeavingDateInput.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.PressAsync("Delete");
        await LeavingDateInput.FillAsync(ddMMyyyy);
        await page.Keyboard.PressAsync("Tab");
    }

    // ── Last Working Day ────────────────────────────────────────────────────────

    private ILocator LastWorkingDayInput => Dialog.Locator(".e-date-wrapper input.e-input").Nth(1);

    public Task<string?> GetLastWorkingDayTextAsync() => LastWorkingDayInput.InputValueAsync();

    public async Task FillLastWorkingDayAsync(string ddMMyyyy)
    {
        await LastWorkingDayInput.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.PressAsync("Delete");
        await LastWorkingDayInput.FillAsync(ddMMyyyy);
        await page.Keyboard.PressAsync("Tab");
    }

    // ── Leaving Reason ──────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the currently-selected Leaving Reason label (e.g. "Resignation", "End of Contract").
    /// SfDropDownList keeps its displayed text on the inner readonly &lt;input&gt;'s value, not as
    /// text content of the outer span[role='combobox'] wrapper (which has none) — mirrors
    /// EmployeeEditPage.GetNoticePeriodUnitTextAsync's established approach for the same control.
    /// </summary>
    public async Task<string?> GetLeavingReasonTextAsync() =>
        (await Dialog.Locator("span[role='combobox']").First.Locator("input").InputValueAsync())?.Trim();

    public Task SelectLeavingReasonAsync(string reasonLabel) =>
        DropDownSelector.SelectAsync(page, Dialog, reasonLabel);

    // ── Actions ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Clicks "Save Changes". Does not assume success — on validation failure (client- or
    /// server-side) the dialog stays open with an inline .alert-danger (see
    /// <see cref="GetErrorAsync"/>); on success the dialog closes and the parent page
    /// force-navigates to "?tab=leaving" (optionally with "&offboardingAlreadyStarted=true").
    /// </summary>
    public async Task SaveAsync()
    {
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Save Changes" }).ClickAsync();

        try
        {
            await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
        }
        catch (TimeoutException)
        {
            await Dialog.Locator(".alert-danger")
                .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 8_000 });
        }
    }

    /// <summary>Dismisses the dialog by clicking Cancel.</summary>
    public async Task CancelAsync()
    {
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
        await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    /// <summary>
    /// Returns the inline error currently shown (client-side "please complete all fields"/"last
    /// working day must be on or before the leaving date", or a server-side rejection), or null
    /// if none is visible.
    /// </summary>
    public async Task<string?> GetErrorAsync()
    {
        var error = Dialog.Locator(".alert-danger").First;
        return await error.IsVisibleAsync() ? (await error.TextContentAsync())?.Trim() : null;
    }
}
