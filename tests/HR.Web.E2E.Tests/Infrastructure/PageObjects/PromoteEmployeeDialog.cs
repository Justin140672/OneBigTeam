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
        var active = Dialog.Locator(".nav-link.active");
        return (await active.TextContentAsync())?.Trim();
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

    public async Task FillEffectiveDateAsync(string ddMMyyyy)
    {
        var input = Dialog.Locator(".e-date-wrapper input.e-input").First;
        await input.ClickAsync();
        await input.FillAsync(ddMMyyyy);
        await page.Keyboard.PressAsync("Tab");
    }

    public Task FillReasonAsync(string reason) =>
        Dialog.GetByPlaceholder("e.g. Annual review promotion").FillAsync(reason);

    public Task FillNotesAsync(string notes) =>
        Dialog.GetByPlaceholder("Optional notes…").FillAsync(notes);

    // ── Step 2: Manager & Location ──────────────────────────────────────────────

    public Task CheckChangeManagerAsync() => Dialog.GetByLabel("Change manager").CheckAsync();

    public Task CheckChangeLocationAsync() => Dialog.GetByLabel("Change location").CheckAsync();

    public Task SelectNewManagerAsync(string managerNameFragment) =>
        DropDownSelector.SelectAsync(page, Dialog.Locator(".col-12").Filter(new() { HasText = "New Manager" }).First, managerNameFragment);

    public Task SelectNewLocationAsync(string locationNameFragment) =>
        DropDownSelector.SelectAsync(page, Dialog.Locator(".col-12").Filter(new() { HasText = "New Location" }).First, locationNameFragment);

    // ── Step 3: Compensation ─────────────────────────────────────────────────────

    public Task CheckCreateCompensationChangeAsync() =>
        Dialog.GetByLabel("Create compensation change").CheckAsync();

    public Task SelectCompensationSalaryTypeAsync(string salaryType) =>
        DropDownSelector.SelectAsync(page, Dialog.Locator(".col-6").Filter(new() { HasText = "Salary Type" }).First, salaryType);

    // Salary/Hours Per Week/FTE are all SfNumericTextBox instances without an explicit
    // FloatLabelType override, so Syncfusion's default (Never) should render Placeholder as a
    // real HTML placeholder attribute — but to avoid relying on that assumption (see
    // EmployeeEditPage's own compensation-dialog fields, which use FloatLabelType.Auto and so
    // can't be targeted by placeholder at all), scope by column position instead: Salary,
    // Hours Per Week and FTE are the only three ".e-numerictextbox" inputs on this step, always
    // rendered in that order (Salary Type is a dropdown, Currency is a plain text box).
    public Task FillCompensationSalaryAsync(string value) =>
        Dialog.Locator("input.e-numerictextbox").Nth(0).FillAsync(value);

    public Task FillCompensationCurrencyAsync(string value) =>
        Dialog.GetByPlaceholder("e.g. GBP").FillAsync(value);

    public Task FillCompensationHoursPerWeekAsync(string value) =>
        Dialog.Locator("input.e-numerictextbox").Nth(1).FillAsync(value);

    public Task FillCompensationFteAsync(string value) =>
        Dialog.Locator("input.e-numerictextbox").Nth(2).FillAsync(value);

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
        var activeStepLocator = Dialog.Locator(".nav-link.active");
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
        }
        catch (TimeoutException)
        {
            await Dialog.Locator(".alert-danger")
                .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 8_000 });
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
