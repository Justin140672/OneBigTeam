using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the "Promote Employee" wizard dialog (PromoteEmployeeDialog.razor), reached
/// from the "Promote" button (data-testid="promote-employee-btn") on the Promotion History tab
/// (see <see cref="EmployeeEditPage"/>'s Promotion History Tab region for opening that tab).
///
/// Four linear steps: 1. Position (new position profile, effective date, reason, notes,
/// read-only current position), 2. Manager &amp; Location (optional "Change manager"/"Change
/// location" checkboxes revealing pickers), 3. Compensation (optional "Create compensation
/// change" checkbox revealing salary fields), 4. Confirm (summary of all pending changes).
///
/// Follows the standalone-page-object-per-component pattern established by
/// <see cref="StartLeavingProcessDialog"/> rather than folding into <see cref="EmployeeEditPage"/>.
/// </summary>
public sealed class PromoteEmployeeDialog(IPage page)
{
    private ILocator Dialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Promote Employee" });
    private ILocator UnsavedChangesDialog => page.Locator("[role='dialog']:has-text('Unsaved Changes')");

    /// <summary>
    /// Clicks the "Promote" button on the (already-open) Promotion History tab and waits for the
    /// wizard dialog to open on step 1.
    /// </summary>
    public async Task OpenAsync()
    {
        await page.Locator("[data-testid='promote-employee-btn']").ClickAsync();
        await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    public Task<bool> IsVisibleAsync() => Dialog.IsVisibleAsync();

    /// <summary>
    /// Returns the trimmed text of the currently-highlighted step in the wizard's step nav
    /// (e.g. "1. Position", "3. Compensation") — used to assert the wizard did NOT advance past
    /// a step whose required field was left blank/invalid.
    /// </summary>
    public async Task<string?> GetActiveStepLabelAsync()
    {
        var active = Dialog.Locator(".hr-stepper-item--current");
        var index = (await active.Locator(".hr-stepper-node").TextContentAsync())?.Trim();
        var label = (await active.Locator(".hr-stepper-label").TextContentAsync())?.Trim();
        return $"{index}. {label}";
    }

    // ── Step 1: Position ────────────────────────────────────────────────────────

    public async Task<string?> GetCurrentPositionTextAsync()
    {
        var input = Dialog.Locator(".col-12").Filter(new() { HasText = "Current Position" })
            .First.Locator("input");
        return (await input.InputValueAsync())?.Trim();
    }

    public Task SelectNewPositionProfileAsync(string profileTitle) =>
        DropDownSelector.SelectAsync(page, Dialog.Locator(".col-12").Filter(new() { HasText = "New Position Profile" }).First, profileTitle);

    /// <summary>
    /// Opens the "New Position Profile" dropdown's popup without selecting anything, returns its
    /// visible option titles, then closes it again — used to assert only vacant (unoccupied)
    /// position profiles are offered (see PromoteEmployeeDialog.razor's OnOpenedAsync, which
    /// excludes any profile currently occupied by another active employee).
    /// </summary>
    public async Task<IReadOnlyList<string>> GetNewPositionProfileDropdownOptionsAsync()
    {
        var field = Dialog.Locator(".col-12").Filter(new() { HasText = "New Position Profile" }).First;
        await field.Locator("span[role='combobox']").First.ClickAsync();
        await page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });

        var items = await page.Locator(".e-popup.e-ddl:visible .e-list-item").AllAsync();
        var titles = new List<string>();
        foreach (var item in items)
            titles.Add((await item.TextContentAsync())?.Trim() ?? "");

        await page.Keyboard.PressAsync("Escape");
        return titles;
    }

    public async Task FillEffectiveDateAsync(string ddMMyyyy)
    {
        var input = Dialog.Locator(".e-date-wrapper input.e-input").First;
        await input.ClickAsync();
        await input.FillAsync(ddMMyyyy);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillReasonAsync(string reason)
    {
        await Dialog.GetByPlaceholder("e.g. Annual review promotion").FillAsync(reason);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillNotesAsync(string notes)
    {
        await Dialog.GetByPlaceholder("Optional notes…").FillAsync(notes);
        await page.Keyboard.PressAsync("Tab");
    }

    // ── Step 2: Manager & Location ──────────────────────────────────────────────

    public Task CheckChangeManagerAsync() => Dialog.GetByLabel("Change manager").CheckAsync();

    public Task CheckChangeLocationAsync() => Dialog.GetByLabel("Change location").CheckAsync();

    public Task SelectNewManagerAsync(string managerNameFragment) =>
        DropDownSelector.SelectAsync(page, Dialog.Locator(".col-12").Filter(new() { HasText = "New Manager" }).First, managerNameFragment);

    public Task SelectNewLocationAsync(string locationNameFragment) =>
        DropDownSelector.SelectAsync(page, Dialog.Locator(".col-12").Filter(new() { HasText = "New Location" }).First, locationNameFragment);

    // ── Step 3: Compensation ─────────────────────────────────────────────────────

    public async Task CheckCreateCompensationChangeAsync()
    {
        await Dialog.GetByLabel("Create compensation change").CheckAsync();
        // Syncfusion's SfCheckBox round-trips its bound value to the server via its own change
        // event a tick after the native input's checked state flips — without waiting for that
        // round-trip, an immediately-following ClickNextAsync can race the Blazor circuit and
        // submit before Model.CreateCompensationChange has actually updated server-side,
        // skipping the compensation-required validation entirely (step 3 -> step 4 in one
        // click). Rather than a fixed sleep (unreliable under concurrent full-suite load), wait
        // for the conditional "Salary Type" field that only renders once
        // Model.CreateCompensationChange is true — a direct signal the round-trip completed.
        await Dialog.GetByText("Salary Type").WaitForAsync(new() { Timeout = 5_000 });
    }

    public Task SelectCompensationSalaryTypeAsync(string salaryType) =>
        DropDownSelector.SelectAsync(page, Dialog.Locator(".col-6").Filter(new() { HasText = "Salary Type" }).First, salaryType);

    // Salary/Hours Per Week/FTE are all SfNumericTextBox instances without an explicit
    // FloatLabelType override, so Syncfusion's default (Never) should render Placeholder as a
    // real HTML placeholder attribute — but to avoid relying on that assumption (see
    // EmployeeEditPage's own compensation-dialog fields, which use FloatLabelType.Auto and so
    // can't be targeted by placeholder at all), scope by column position instead: Salary,
    // Hours Per Week and FTE are the only three ".e-numerictextbox" inputs on this step, always
    // rendered in that order (Salary Type is a dropdown, Currency is a plain text box).
    // SfNumericTextBox: a bare FillAsync bypasses its interop entirely (see EmployeeEditPage.
    // TypeIntoNumericInputAsync for the same pattern/explanation) — retype the value for real.
    private async Task TypeIntoNumericInputAsync(ILocator input, string value)
    {
        await input.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.PressAsync("Delete");
        if (value.Length > 0)
            await input.PressSequentiallyAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public Task FillCompensationSalaryAsync(string value) =>
        TypeIntoNumericInputAsync(Dialog.Locator("input.e-numerictextbox").Nth(0), value);

    public async Task FillCompensationCurrencyAsync(string value)
    {
        await Dialog.GetByPlaceholder("e.g. GBP").FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public Task FillCompensationHoursPerWeekAsync(string value) =>
        TypeIntoNumericInputAsync(Dialog.Locator("input.e-numerictextbox").Nth(1), value);

    public Task FillCompensationFteAsync(string value) =>
        TypeIntoNumericInputAsync(Dialog.Locator("input.e-numerictextbox").Nth(2), value);

    // ── Step 4: Confirmation summary ────────────────────────────────────────────

    /// <summary>
    /// Returns the &lt;dd&gt; text immediately following the &lt;dt&gt; whose text contains
    /// <paramref name="label"/> (e.g. "New Position", "Reason", "New Manager", "New Location",
    /// "Compensation Change") in the step 4 confirmation summary, or null if that label isn't
    /// currently rendered (e.g. "New Manager" only appears when Model.ChangeManager is true).
    /// </summary>
    public async Task<string?> GetConfirmationValueAsync(string label)
    {
        var dt = Dialog.Locator("dl.row dt").Filter(new() { HasText = label }).First;
        if (!await dt.IsVisibleAsync())
            return null;
        var dd = dt.Locator("xpath=following-sibling::dd[1]");
        return (await dd.TextContentAsync())?.Trim();
    }

    // ── Navigation ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Clicks "Next" (steps 1-3). Does not assume the wizard actually advances — a missing/
    /// invalid required field (or an unfilled optional compensation section) keeps it on the
    /// same step with an inline error instead (see <see cref="GetGlobalErrorAsync"/>).
    /// </summary>
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

    /// <summary>Clicks "Back" (steps 2-4).</summary>
    public Task ClickBackAsync() =>
        Dialog.GetByRole(AriaRole.Button, new() { Name = "Back" }).ClickAsync();

    /// <summary>
    /// Clicks the step 4 submit button. Its accessible name is "Promote" normally, or
    /// "Confirm &amp; Promote" once the server has asked for backdate confirmation — matching by
    /// the (non-exact) substring "Promote" catches both. Does not assume success — on server-side
    /// failure the dialog stays open on the confirmation step with an inline .alert-danger (see
    /// <see cref="GetGlobalErrorAsync"/>), which may also flip the button's own text to
    /// "Confirm &amp; Promote" for a subsequent resubmit.
    /// </summary>
    public async Task SubmitAsync()
    {
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Promote" }).ClickAsync();

        try
        {
            await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
            await WaitForOverlayToClearAsync();
        }
        catch (TimeoutException)
        {
            await Dialog.Locator(".alert-danger")
                .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 8_000 });

            // A backdated effective date makes the server ask for confirmation instead of
            // promoting immediately (PromoteEmployeeDialog.razor's _awaitingBackdateConfirmation),
            // which re-labels the submit button "Confirm & Promote" rather than closing the
            // dialog. A caller expecting a genuine validation failure (not a backdate confirmation)
            // will still see the dialog open with .alert-danger visible, same as before; a caller
            // that hit the backdate path gets the confirmation click it needs to actually complete.
            var confirmButton = Dialog.GetByRole(AriaRole.Button, new() { Name = "Confirm & Promote" });
            if (await confirmButton.IsVisibleAsync())
            {
                await confirmButton.ClickAsync();
                await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
                await WaitForOverlayToClearAsync();
            }
        }
    }

    /// <summary>
    /// Syncfusion's modal overlay (".e-dlg-overlay") is a DOM sibling of the dialog itself, not a
    /// descendant, and its close animation can still be mid-fade (still intercepting pointer
    /// events) for a short moment after the dialog role element itself already reports "Hidden" to
    /// Playwright (not visible/detached). A caller that immediately clicks something else (e.g. a
    /// tab) right after this dialog closes can otherwise hit "subtree intercepts pointer events" on
    /// the stale overlay. Best-effort: if no overlay is present at all, this is a no-op.
    /// </summary>
    private async Task WaitForOverlayToClearAsync()
    {
        try
        {
            await page.Locator(".e-dlg-overlay").WaitForAsync(
                new() { State = WaitForSelectorState.Detached, Timeout = 5_000 });
        }
        catch (TimeoutException)
        {
            // Ignore — best-effort settle only.
        }
    }

    /// <summary>Dismisses the dialog by clicking Cancel (available on every step).</summary>
    public async Task CancelAsync()
    {
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();    }

    public async Task ConfirmDiscardChangesAsync()
    {
        await UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Discard Changes" }).ClickAsync();
        await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    /// <summary>
    /// Returns the inline error banner (PromoteEmployeeDialog's own GlobalError, shown for both
    /// client-side step validation failures and server-side submission failures), or null if none
    /// is currently visible.
    /// </summary>
    public async Task<string?> GetGlobalErrorAsync()
    {
        var error = Dialog.Locator(".alert-danger").First;
        return await error.IsVisibleAsync() ? (await error.TextContentAsync())?.Trim() : null;
    }
}
