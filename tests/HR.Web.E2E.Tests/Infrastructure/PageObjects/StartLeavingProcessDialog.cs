using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the "Start Leaving Process" wizard dialog
/// (StartLeavingProcessDialog.razor), reached from the Employee Overview header's "Start
/// Leaving Process" button (only visible while no leaving process is active — see
/// EmployeeEdit.razor's _showLeavingTab). Five linear steps, no ability to jump around:
/// 1. Resignation Received Date, 2. Leaving Date (auto-computed from the employee's effective
/// notice period, but editable), 3. Last Working Day, 4. Leaving Reason, 5. Confirm.
///
/// Follows the standalone-page-object-per-component pattern established by
/// <see cref="EmployeeOffboardingTab"/> rather than folding into <see cref="EmployeeEditPage"/>.
/// </summary>
public sealed class StartLeavingProcessDialog(IPage page)
{
    private ILocator Dialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Start Leaving Process" });

    /// <summary>
    /// Opens the "More actions" overflow menu and clicks its "Start offboarding" item, waiting for
    /// the (still internally/dialog-titled "Start Leaving Process") dialog to open. The header
    /// action itself was moved into the "More actions" dropdown and renamed "Start offboarding" —
    /// see EmployeeEdit.razor's BuildMoreActionsItems/HandleMoreActionSelected — but the dialog's
    /// own <c>&lt;Header&gt;</c> text is unchanged.
    /// </summary>
    public async Task OpenAsync()
    {
        var moreActionsButton = page.GetByRole(AriaRole.Button, new() { Name = "More actions" });
        var menuItem = page.GetByRole(AriaRole.Menuitem, new() { Name = "Start offboarding" });

        // The SfDropDownButton's popup (".e-dropdown-popup") is rendered asynchronously after the
        // click event dispatches — a bare click-then-click race can fire the menu item click before
        // Syncfusion has actually attached its own click handler to the just-rendered item, silently
        // swallowing the click and leaving the "Start Leaving Process" dialog never opened. Retry the
        // whole open sequence (button click + explicit visible-wait on the item) rather than trusting
        // a single attempt, mirroring the retry patterns already used elsewhere in this suite for the
        // same class of freshly-mounted-widget race.
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await moreActionsButton.ClickAsync();
            try
            {
                await menuItem.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
            }
            catch (TimeoutException)
            {
                // Popup may not have opened (or already closed) — press Escape to reset and retry.
                await page.Keyboard.PressAsync("Escape");
                continue;
            }

            await menuItem.ClickAsync();

            try
            {
                await Dialog.WaitForAsync(new() { Timeout = 8_000 });
                return;
            }
            catch (TimeoutException)
            {
                // Click may have been swallowed by the popup closing mid-interop-attach — retry.
            }
        }

        // Final attempt with the full original timeout, so a genuine failure still surfaces with a
        // clear, undiminished timeout budget rather than the shorter per-retry ones above.
        await moreActionsButton.ClickAsync();
        await menuItem.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
        await menuItem.ClickAsync();
        await Dialog.WaitForAsync(new() { Timeout = 15_000 });
    }

    public Task<bool> IsVisibleAsync() => Dialog.IsVisibleAsync();

    /// <summary>
    /// Returns the trimmed text of the currently-highlighted step in the wizard's step nav
    /// (e.g. "1. Resignation Date", "4. Reason") — used to assert the wizard did NOT advance
    /// past a step whose required field was left blank.
    /// </summary>
    public async Task<string?> GetActiveStepLabelAsync()
    {
        var active = Dialog.Locator(".hr-stepper-item--current");
        var index = (await active.Locator(".hr-stepper-node").TextContentAsync())?.Trim();
        var label = (await active.Locator(".hr-stepper-label").TextContentAsync())?.Trim();
        return $"{index}. {label}";
    }

    // ── Step 1: Resignation Received Date ──────────────────────────────────────

    public async Task FillResignationReceivedDateAsync(string ddMMyyyy)
    {
        var input = Dialog.Locator(".e-date-wrapper input.e-input").First;
        await input.ClickAsync();
        await input.FillAsync(ddMMyyyy);
        await page.Keyboard.PressAsync("Tab");
    }

    // ── Step 2: Leaving Date (auto-computed, editable) ─────────────────────────

    /// <summary>
    /// Reads the auto-computed (or since-amended) Leaving Date value on step 2 — used to verify
    /// StartLeavingProcessDialog.ComputeProposedLeavingDate actually populated something before
    /// the user amends or accepts it.
    /// </summary>
    public async Task<string?> GetLeavingDateTextAsync()
    {
        var input = Dialog.Locator(".e-date-wrapper input.e-input").First;
        return (await input.InputValueAsync())?.Trim();
    }

    /// <summary>Overwrites the auto-computed Leaving Date on step 2 with an explicit value.</summary>
    public async Task FillLeavingDateAsync(string ddMMyyyy)
    {
        var input = Dialog.Locator(".e-date-wrapper input.e-input").First;
        await input.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.PressAsync("Delete");
        await input.FillAsync(ddMMyyyy);
        await page.Keyboard.PressAsync("Tab");
    }

    /// <summary>
    /// Clears the (auto-computed) Leaving Date on step 2 so the "Please select a leaving date."
    /// required-field validation can be exercised — step 2 otherwise arrives pre-populated from
    /// StartLeavingProcessDialog.ComputeProposedLeavingDate.
    /// </summary>
    public async Task ClearLeavingDateAsync()
    {
        var input = Dialog.Locator(".e-date-wrapper input.e-input").First;
        await input.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.PressAsync("Delete");
        await page.Keyboard.PressAsync("Tab");
    }

    // ── Step 3: Last Working Day ────────────────────────────────────────────────

    public async Task FillLastWorkingDayAsync(string ddMMyyyy)
    {
        var input = Dialog.Locator(".e-date-wrapper input.e-input").First;
        await input.ClickAsync();
        await input.FillAsync(ddMMyyyy);
        await page.Keyboard.PressAsync("Tab");
    }

    // ── Step 4: Leaving Reason ──────────────────────────────────────────────────

    /// <summary>
    /// Selects a value from the required Leaving Reason dropdown (e.g. "Resignation",
    /// "Redundancy", "End of Contract" — the friendly labels from
    /// EmployeeLeavingTab.LeavingReasonLabel, not the raw enum-like values).
    /// </summary>
    public Task SelectLeavingReasonAsync(string reasonLabel) =>
        DropDownSelector.SelectAsync(page, Dialog, reasonLabel);

    // ── Step 5: Confirmation summary ────────────────────────────────────────────

    private ILocator ConfirmationSummary => Dialog.Locator("dl.row");

    public async Task<string?> GetConfirmationResignationReceivedDateTextAsync() =>
        (await ConfirmationSummary.Locator("dd").Nth(0).TextContentAsync())?.Trim();

    public async Task<string?> GetConfirmationLeavingDateTextAsync() =>
        (await ConfirmationSummary.Locator("dd").Nth(1).TextContentAsync())?.Trim();

    public async Task<string?> GetConfirmationLastWorkingDayTextAsync() =>
        (await ConfirmationSummary.Locator("dd").Nth(2).TextContentAsync())?.Trim();

    public async Task<string?> GetConfirmationLeavingReasonTextAsync() =>
        (await ConfirmationSummary.Locator("dd").Nth(3).TextContentAsync())?.Trim();

    // ── Navigation ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Clicks "Next" (steps 1-4). Does not assume the wizard actually advances — a missing/
    /// invalid required field keeps it on the same step with an inline error instead (see
    /// <see cref="GetStepErrorAsync"/>).
    /// </summary>
    /// <remarks>
    /// GoNext() is a synchronous server-side handler (no I/O), but the round trip back through
    /// Blazor's circuit to actually update the DOM — either highlighting a new active step, or
    /// rendering an inline validation error while staying on the same step — is still async from
    /// Playwright's perspective. A bare ClickAsync only waits for the click event to dispatch,
    /// not for that round trip, so a caller that immediately reads the next step's fields (or the
    /// error message) can race ahead of it. Wait for the success case (active step label
    /// changing) first; if that doesn't happen in time, fall back to waiting for the validation
    /// error instead — mirrors <see cref="ConfirmAsync"/>'s try/catch pattern below, and lets this
    /// same method be reused for both the happy path and the "missing required field" tests.
    /// </remarks>
    public async Task ClickNextAsync()
    {
        var activeStepLocator = Dialog.Locator(".hr-stepper-item--current");
        var beforeLabel = (await activeStepLocator.TextContentAsync())?.Trim() ?? string.Empty;

        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Next" }).ClickAsync();

        try
        {
            await Assertions.Expect(activeStepLocator).Not.ToHaveTextAsync(beforeLabel, new() { Timeout = 8_000 });
        }
        catch (PlaywrightException)
        {
            await Dialog.Locator(".alert-danger")
                .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 8_000 });
        }
    }

    /// <summary>Clicks "Back" (steps 2-5).</summary>
    public Task ClickBackAsync() =>
        Dialog.GetByRole(AriaRole.Button, new() { Name = "Back" }).ClickAsync();

    /// <summary>
    /// Clicks "Confirm" on step 5 (already open, already filled). Does not assume success — on
    /// server-side failure the dialog stays open on the confirmation step with an inline
    /// .alert-danger (see <see cref="GetGlobalErrorAsync"/>); on success the dialog closes and
    /// the parent page force-navigates to "?tab=leaving".
    /// </summary>
    public async Task ConfirmAsync()
    {
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Confirm" }).ClickAsync();

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
    /// Returns the inline client-side step-validation error (StartLeavingProcessDialog's own
    /// `_stepError`, e.g. "Please select a leaving reason.") currently shown, or null if none is
    /// visible. Distinct from <see cref="GetGlobalErrorAsync"/>, which surfaces server-side
    /// failures from the Confirm step instead.
    /// </summary>
    public async Task<string?> GetStepErrorAsync()
    {
        var error = Dialog.Locator(".alert-danger").First;
        return await error.IsVisibleAsync() ? (await error.TextContentAsync())?.Trim() : null;
    }

    /// <summary>
    /// Returns the inline server-side error (StartLeavingProcessDialog's own `_globalError`)
    /// shown after an unsuccessful <see cref="ConfirmAsync"/> call, or null if none is visible.
    /// </summary>
    public Task<string?> GetGlobalErrorAsync() => GetStepErrorAsync();
}
