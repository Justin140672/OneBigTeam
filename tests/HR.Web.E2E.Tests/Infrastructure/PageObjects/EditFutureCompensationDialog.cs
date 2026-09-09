using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the "Edit Future Compensation" dialog (EditFutureCompensationDialog.razor),
/// opened from the Compensation History grid's per-row "Edit" action on a future-dated record
/// (see <see cref="EmployeeEditPage.ClickEditCompensationRowAsync"/>).
///
/// Covers the Ticket 2 optimistic-concurrency conflict UI: when the save is rejected with an
/// HTTP 409 because the record's Version moved on since the dialog loaded it, the dialog renders
/// the shared &lt;SaveConflictBanner&gt; — a single
/// <c>div.alert.alert-warning.save-conflict-banner[role='alert']</c> inside the dialog, carrying
/// the razor's own message ("Someone else changed this compensation record while you were
/// editing…") and a "Reload latest values" Syncfusion button. "Reload latest values" re-fetches
/// the record, repopulates the form with the competing editor's values, adopts the fresh Version
/// and clears the banner, after which a re-save succeeds.
///
/// Scoped throughout to the dialog's own <c>.edit-future-compensation-dialog</c> CssClass (which
/// Syncfusion also stamps on the outer container / close button — hence pairing it with
/// <c>[role='dialog']</c> or a descendant selector, never the bare class alone) so a second
/// browser tab's dialog on the same page can never satisfy strict mode here. The conflict-banner
/// locator additionally requires the "Reload latest values" action, matching on structure rather
/// than the (razor-overridden) message text.
/// </summary>
public sealed class EditFutureCompensationDialog(IPage page)
{
    private ILocator Dialog => page.Locator("[role='dialog'].edit-future-compensation-dialog");

    private ILocator SalaryInput => page.Locator(".edit-future-compensation-dialog input.e-numerictextbox").First;

    private ILocator SaveButton =>
        page.Locator(".edit-future-compensation-dialog .e-footer-content button:has-text('Save')");

    // Bootstrap's `.alert-warning` alone is shared markup; scope on the component's own
    // `.save-conflict-banner` class + role='alert' and additionally require the "Reload latest
    // values" button so an unrelated warning alert can never satisfy strict mode. Match on
    // structure, not text — a real 409 replaces the component's default Message with the razor's
    // own copy, so a text filter would be brittle.
    private ILocator ConcurrencyWarningBanner =>
        page.Locator(".edit-future-compensation-dialog .save-conflict-banner[role='alert']")
            .Filter(new() { Has = page.GetByRole(AriaRole.Button, new() { Name = "Reload latest values" }) });

    /// <summary>Waits for the dialog (and its Salary field) to be interactable after the row's Edit action.</summary>
    public async Task WaitForOpenAsync()
    {
        await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await Microsoft.Playwright.Assertions.Expect(SalaryInput).ToBeEnabledAsync(new() { Timeout = 30_000 });
    }

    /// <summary>
    /// Sets the Salary field (a FloatLabelType SfNumericTextBox — targeted by its e-numerictextbox
    /// class, not a placeholder) and confirms the parsed value stuck, retrying under a laggy Blazor
    /// Server round-trip. Mirrors EmployeeEditPage.FillEditCompensationSalaryAsync.
    /// </summary>
    public async Task FillSalaryAsync(decimal value)
    {
        var text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await SalaryInput.ClickAsync();
            await page.Keyboard.PressAsync("Control+A");
            await page.Keyboard.PressAsync("Delete");
            await page.WaitForTimeoutAsync(150);
            await SalaryInput.PressSequentiallyAsync(text, new() { Delay = 30 });
            await page.Keyboard.PressAsync("Tab");
            await page.WaitForTimeoutAsync(200);

            if (await GetSalaryValueAsync() == value)
                return;

            if (attempt < 3)
                await page.WaitForTimeoutAsync(250);
        }

        throw new PlaywrightException(
            $"Salary did not stick after 3 attempts: expected '{value}', got '{await SalaryInput.InputValueAsync()}'.");
    }

    public async Task<decimal?> GetSalaryValueAsync()
    {
        var raw = (await SalaryInput.InputValueAsync())?.Replace(",", "");
        return decimal.TryParse(raw, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    /// <summary>Clicks Save and waits for the dialog to close — the save succeeded.</summary>
    public async Task SubmitExpectingSuccessAsync()
    {
        await SaveButton.ClickAsync();
        await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });

        // The dialog closing only proves the save request was accepted, not that the Compensation
        // History grid's own async reload has landed — a caller that immediately reads the row can
        // otherwise still see the pre-edit value (same race handled in
        // EmployeeEditPage.SubmitEditCompensationDialogAsync).
        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        await page.WaitForTimeoutAsync(300);
    }

    /// <summary>
    /// Clicks Save and waits for the optimistic-concurrency banner to appear inside the dialog —
    /// i.e. the save was rejected because the record changed since the dialog loaded it. The dialog
    /// stays open and the caller's entered values are left untouched.
    /// </summary>
    public async Task SubmitExpectingConflictAsync()
    {
        await SaveButton.ClickAsync();
        await ConcurrencyWarningBanner.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 20_000 });
    }

    public Task<bool> IsConcurrencyWarningVisibleAsync() =>
        ConcurrencyWarningBanner.IsVisibleAsync();

    /// <summary>Clicks "Reload latest values" in the banner and waits for it to clear.</summary>
    public async Task ClickReloadLatestValuesAsync()
    {
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Reload latest values" }).ClickAsync();
        await ConcurrencyWarningBanner.WaitForAsync(
            new() { State = WaitForSelectorState.Hidden, Timeout = 20_000 });
        // The reload round-trips CompensationService.GetCompensationHistoryAsync before
        // repopulating the form; give the re-bound value a beat to land before callers read it.
        await page.WaitForTimeoutAsync(300);
    }

    public Task<bool> IsOpenAsync() => Dialog.IsVisibleAsync();
}
