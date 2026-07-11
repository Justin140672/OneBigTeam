using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the Offboarding tab on the employee edit page
/// (EmployeeOffboardingTab.razor). Covers the empty state, the "Start Offboarding" dialog,
/// and the resulting plan summary / checklist once a plan exists.
///
/// Unlike the Onboarding tab (auto-created via a domain event on employee creation), an
/// Offboarding plan only exists once "Start Offboarding" has been submitted successfully —
/// there is no seed/auto-provisioning path to rely on.
///
/// Follows the standalone-tab-page-object pattern established by <see cref="ContactDetailsTab"/>
/// rather than being folded into <see cref="EmployeeEditPage"/>.
/// </summary>
public sealed class EmployeeOffboardingTab(IPage page)
{
    public async Task OpenAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Offboarding" }).ClickAsync();
        // Wait for the tab content to render — either the progress panel's progress bar
        // (a plan exists) or the "No offboarding plan found for this employee" empty state.
        await page.WaitForSelectorAsync(".progress, .hr-empty-state", new() { Timeout = 15_000 });
    }

    // ── Empty state ──────────────────────────────────────────────────────────────

    /// <summary>Returns true if the "No offboarding plan found for this employee" empty state is visible.</summary>
    public async Task<bool> IsEmptyStateVisibleAsync() =>
        await page.Locator(".hr-empty-state").IsVisibleAsync();

    /// <summary>Returns true if the "Start Offboarding" button is visible.</summary>
    public async Task<bool> HasStartOffboardingButtonAsync() =>
        await page.GetByRole(AriaRole.Button, new() { Name = "Start Offboarding" }).IsVisibleAsync();

    // ── Start Offboarding dialog ─────────────────────────────────────────────────

    /// <summary>
    /// Opens the Start Offboarding dialog and waits for it to render.
    /// Scoped via aria role + accessible name (Header="Start Offboarding") since SfDialog
    /// carries no distinguishing CssClass here — mirrors EmployeeAdminPage's
    /// OpenAssignAssetDialogAsync pattern for the "Assign Asset" dialog.
    /// </summary>
    public async Task OpenStartDialogAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Start Offboarding" }).ClickAsync();
        await page.GetByRole(AriaRole.Dialog, new() { Name = "Start Offboarding" })
            .WaitForAsync(new() { Timeout = 15_000 });
    }

    /// <summary>Returns true if the Start Offboarding dialog is currently visible.</summary>
    public async Task<bool> IsStartDialogVisibleAsync() =>
        await page.GetByRole(AriaRole.Dialog, new() { Name = "Start Offboarding" }).IsVisibleAsync();

    /// <summary>Fills the required "Last Working Day" date picker in the (already open) dialog.</summary>
    public async Task FillLastWorkingDayAsync(string ddMMyyyy)
    {
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Start Offboarding" });
        var input = dialog.Locator(".e-date-wrapper input.e-input");
        await input.ClickAsync();
        await input.FillAsync(ddMMyyyy);
        await page.Keyboard.PressAsync("Tab");
    }

    /// <summary>Fills the optional "Notes" textarea in the (already open) dialog.</summary>
    public async Task FillNotesAsync(string notes)
    {
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Start Offboarding" });
        await dialog.Locator("textarea").FillAsync(notes);
    }

    /// <summary>
    /// Clicks Start on the (already open, already filled) dialog. Does not assume success —
    /// on validation/API failure the dialog stays open with an inline .alert-danger (see
    /// <see cref="GetStartDialogErrorAsync"/>); on success the dialog closes and the tab
    /// reloads its overview.
    /// </summary>
    public async Task SubmitStartAsync()
    {
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Start Offboarding" });
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Start" }).ClickAsync();

        try
        {
            await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
        }
        catch (TimeoutException)
        {
            await dialog.Locator(".alert-danger")
                .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 8_000 });
        }
    }

    /// <summary>Dismisses the Start Offboarding dialog by clicking Cancel.</summary>
    public async Task CancelStartDialogAsync()
    {
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Start Offboarding" });
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    /// <summary>
    /// Returns the inline validation/error text (component's local `_dialogError`, rendered as
    /// an .alert-danger inside the dialog content) currently shown in the Start Offboarding
    /// dialog, or null if none is visible. Used for both the client-side "please select a last
    /// working day" case and any server-side conflict error.
    /// </summary>
    public async Task<string?> GetStartDialogErrorAsync()
    {
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Start Offboarding" });
        var error = dialog.Locator(".alert-danger");
        return await error.IsVisibleAsync() ? (await error.TextContentAsync())?.Trim() : null;
    }

    // ── Plan overview (progress panel + checklist) ───────────────────────────────

    /// <summary>Returns true if the offboarding progress panel (status badge + progress bar) is visible.</summary>
    public async Task<bool> HasProgressPanelAsync() =>
        await page.Locator(".progress").IsVisibleAsync();

    /// <summary>Returns true if the Offboarding Checklist card is visible.</summary>
    public async Task<bool> HasChecklistCardAsync() =>
        await page.Locator(".card-header:has-text('Offboarding Checklist')").IsVisibleAsync();

    /// <summary>Returns the text of the offboarding plan status badge on the progress panel.</summary>
    public async Task<string?> GetStatusBadgeTextAsync()
    {
        var badge = page.Locator(".card .badge").First;
        return await badge.IsVisibleAsync() ? (await badge.TextContentAsync())?.Trim() : null;
    }

    /// <summary>
    /// Returns the current offboarding progress percentage, read from the progress bar's
    /// aria-valuenow attribute (more robust than scraping the "NN%" caption text).
    /// </summary>
    public async Task<int> GetProgressPercentAsync()
    {
        var bar = page.Locator(".progress .progress-bar");
        var value = await bar.GetAttributeAsync("aria-valuenow");
        return int.TryParse(value, out var percent) ? percent : 0;
    }

    /// <summary>
    /// Returns the status badge text ("Pending"/"In Progress"/"Completed"/"Skipped"/"Overdue")
    /// for the Offboarding Checklist row whose Task cell contains <paramref name="taskTitleFragment"/>.
    /// Scoped to the "Offboarding Checklist" card specifically, mirroring
    /// EmployeeEditPage.GetOnboardingChecklistTaskStatusAsync's scoping-by-card-then-by-row-then-by-badge
    /// technique.
    /// </summary>
    public async Task<string?> GetChecklistTaskStatusAsync(string taskTitleFragment)
    {
        var checklistCard = page.Locator(".card").Filter(new() { HasText = "Offboarding Checklist" }).First;
        var row = checklistCard.Locator("table tbody tr").Filter(new() { HasText = taskTitleFragment }).First;
        var badge = row.Locator(".badge");
        return await badge.IsVisibleAsync() ? (await badge.TextContentAsync())?.Trim() : null;
    }

    /// <summary>Returns true if any row in the Offboarding Checklist table's Task column contains <paramref name="taskTitleFragment"/>.</summary>
    public async Task<bool> HasChecklistTaskAsync(string taskTitleFragment)
    {
        var checklistCard = page.Locator(".card").Filter(new() { HasText = "Offboarding Checklist" }).First;
        return await checklistCard.Locator("table tbody tr")
            .Filter(new() { HasText = taskTitleFragment })
            .First
            .IsVisibleAsync();
    }
}
