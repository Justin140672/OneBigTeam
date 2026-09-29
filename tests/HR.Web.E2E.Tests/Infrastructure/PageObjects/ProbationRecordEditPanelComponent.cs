using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the Probation tab's inline administrative-correction editor
/// (ProbationRecordEditPanel.razor / EmployeeProbationTab.razor's "Edit" button), reached via
/// EmployeeEditPage.OpenProbationTabAsync(). Unlike EditFutureCompensationDialog this panel is not
/// a Syncfusion dialog — it swaps in-place for the read-only &lt;dl&gt; summary inside the
/// "Probation Record" card.
///
/// EmployeeEdit hosts multiple tabs (Personal Details' DOB date picker, Employment's HR Notes
/// textarea, etc.) that stay mounted in the DOM — just hidden — while another tab is active.
/// Unscoped page-wide locators like ".e-date-wrapper input.e-input" or "textarea" previously
/// resolved to one of those *other*, hidden fields instead of this panel's own, causing Playwright
/// "element is not visible" timeouts (not a real render-order race). Every locator here is
/// therefore scoped to the panel's own data-testid wrapper and its fields' own explicit ids
/// (probation-manager-input / probation-expected-end-date-input / probation-notes-input).
/// </summary>
public sealed class ProbationRecordEditPanelComponent(IPage page)
{
    private ILocator EditButton => page.Locator("[data-testid='edit-probation-record']");
    private ILocator Panel => page.Locator("[data-testid='probation-record-edit-panel']");
    private ILocator SaveButton => Panel.Locator("[data-testid='save-probation-record']");
    private ILocator ManagerSummary => page.Locator("[data-testid='probation-manager']");
    private ILocator ExpectedEndDateSummary => page.Locator("[data-testid='probation-expected-end-date']");
    private ILocator NotesSummary => page.Locator("[data-testid='probation-notes']");

    private ILocator ManagerCombobox => Panel.Locator("#probation-manager-input");
    private ILocator DatePickerInput => Panel.Locator("#probation-expected-end-date-input");
    private ILocator NotesTextArea => Panel.Locator("#probation-notes-input");
    private ILocator SuccessAlert => Panel.Locator(".alert-success[role='status']");

    private ILocator ConflictBanner =>
        Panel.Locator(".save-conflict-banner[role='alert']")
            .Filter(new() { Has = page.GetByRole(AriaRole.Button, new() { Name = "Reload latest values" }) });

    // Ticket 18: the panel's plain business-rule error (GlobalError, shown only when SaveConflict
    // is false — see ProbationRecordEditPanel.razor) — distinct from ConflictBanner above, which is
    // only rendered for a genuine optimistic-concurrency rejection.
    private ILocator GlobalErrorAlert => Panel.Locator(".alert-danger");

    // Ticket 18: rendered by EmployeeProbationTab.razor (not this panel) once a conflict-reload
    // discovers the record has become terminal and the panel has exited — see OnBecameTerminal.
    private ILocator TerminalWhileEditingWarning =>
        page.Locator("[data-testid='probation-record-terminal-while-editing']");

    public Task<bool> IsEditButtonVisibleAsync() => EditButton.IsVisibleAsync();

    public async Task ClickEditAsync()
    {
        await EditButton.ClickAsync();
        await SaveButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    private ILocator ManagerFieldGroup => Panel.Locator(".col-md-6").Filter(new() { HasText = "Manager" }).First;

    public Task SelectManagerAsync(string managerNameFragment) =>
        DropDownSelector.SelectAsync(page, ManagerFieldGroup, managerNameFragment);

    public async Task SetExpectedEndDateAsync(string ddMMyyyy)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await DatePickerInput.ClickAsync();
            await DatePickerInput.FillAsync("");
            await DatePickerInput.FillAsync(ddMMyyyy);
            await page.Keyboard.PressAsync("Tab");
            await page.WaitForTimeoutAsync(200);

            var actual = await DatePickerInput.InputValueAsync();
            if (!string.IsNullOrWhiteSpace(actual) && actual.Contains(ddMMyyyy[^4..]))
                return;

            if (attempt < 3)
                await page.WaitForTimeoutAsync(250);
        }
    }

    public async Task SetNotesAsync(string notes)
    {
        await NotesTextArea.ClickAsync();
        await NotesTextArea.FillAsync("");
        await NotesTextArea.FillAsync(notes);
        await page.Keyboard.PressAsync("Tab");
        await page.WaitForTimeoutAsync(150);
    }

    public async Task SaveExpectingSuccessAsync()
    {
        await SaveButton.ClickAsync();
        await SuccessAlert.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    /// <summary>Clicks Save and waits for the conflict banner — the save was rejected with HTTP 409.</summary>
    public async Task SaveExpectingConflictAsync()
    {
        await SaveButton.ClickAsync();
        await ConflictBanner.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 20_000 });
    }

    public Task<bool> IsConflictBannerVisibleAsync() => ConflictBanner.IsVisibleAsync();

    public Task<bool> IsSuccessMessageVisibleAsync() => SuccessAlert.IsVisibleAsync();

    public async Task ClickReloadLatestValuesAsync()
    {
        await ConflictBanner.GetByRole(AriaRole.Button, new() { Name = "Reload latest values" }).ClickAsync();
        await ConflictBanner.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 20_000 });
        await SaveButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await page.WaitForTimeoutAsync(300);
    }

    public Task<string> GetManagerFieldValueAsync() => ManagerCombobox.InputValueAsync()!;

    public Task<string?> GetExpectedEndDateFieldValueAsync() => DatePickerInput.InputValueAsync();

    public async Task<string?> GetManagerSummaryTextAsync() =>
        await ManagerSummary.IsVisibleAsync() ? (await ManagerSummary.TextContentAsync())?.Trim() : null;

    public async Task<string?> GetExpectedEndDateSummaryTextAsync() =>
        await ExpectedEndDateSummary.IsVisibleAsync() ? (await ExpectedEndDateSummary.TextContentAsync())?.Trim() : null;

    public async Task<string?> GetNotesSummaryTextAsync() =>
        await NotesSummary.IsVisibleAsync() ? (await NotesSummary.TextContentAsync())?.Trim() : null;

    // ── Ticket 18: business-rule conflict (terminal status) vs. concurrency conflict ──────────────

    public async Task SaveExpectingBusinessErrorAsync()
    {
        await SaveButton.ClickAsync();
        await GlobalErrorAlert.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 20_000 });
    }

    public Task<bool> IsGlobalErrorVisibleAsync() => GlobalErrorAlert.IsVisibleAsync();

    public async Task<string?> GetGlobalErrorTextAsync() =>
        await GlobalErrorAlert.IsVisibleAsync() ? (await GlobalErrorAlert.TextContentAsync())?.Trim() : null;

    public async Task ClickReloadLatestValuesExpectingTerminalExitAsync()
    {
        await ConflictBanner.GetByRole(AriaRole.Button, new() { Name = "Reload latest values" }).ClickAsync();
        await Panel.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 20_000 });
    }

    public Task<bool> IsTerminalWhileEditingWarningVisibleAsync() => TerminalWhileEditingWarning.IsVisibleAsync();

    public async Task<string?> GetTerminalWhileEditingWarningTextAsync() =>
        await TerminalWhileEditingWarning.IsVisibleAsync()
            ? (await TerminalWhileEditingWarning.TextContentAsync())?.Trim()
            : null;
}
