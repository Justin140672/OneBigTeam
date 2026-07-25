using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the Bulk Update dialog opened from the Employee List toolbar
/// (Components/Pages/Employees/BulkCompensationUpdateDialog.razor), which wraps
/// BulkCompensationAdjustmentPanel.razor (SectionHeading="Adjustment Details") in an SfDialog.
///
/// All locators are scoped to the dialog element itself (identified by its own CssClass,
/// "bulk-compensation-update-dialog", combined with role='dialog' to avoid matching the close
/// button/outer container Syncfusion also stamps with the same CssClass) so they can't collide
/// with same-named controls on the underlying Employee List page or elsewhere.
/// </summary>
public sealed class BulkCompensationUpdateDialogPage(IPage page)
{
    private ILocator Dialog => page.Locator("[role='dialog'].bulk-compensation-update-dialog");

    public Task<bool> IsOpenAsync() => Dialog.IsVisibleAsync();

    public Task<string?> GetSelectedEmployeesSummaryAsync() =>
        Dialog.Locator("p.text-muted").First.TextContentAsync();

    // ── Adjustment Details ───────────────────────────────────────────────────────

    /// <summary>
    /// Selects the Adjustment Mode dropdown (the first of two SfDropDownLists in the panel — the
    /// second is Reason). Value must match one of the ModeOptions labels exactly, e.g.
    /// "Percentage Increase", "Fixed Amount Increase", "Set Salary Directly".
    /// </summary>
    public async Task SelectModeAsync(string modeLabel)
    {
        await Dialog.Locator("span[role='combobox']").First.ClickAsync();
        await page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });
        await page.Locator(".e-popup.e-ddl .e-list-item").Filter(new() { HasText = modeLabel }).First.ClickAsync();
    }

    /// <summary>Selects the Reason dropdown (the second of two SfDropDownLists in the panel).</summary>
    public async Task SelectReasonAsync(string reasonLabel)
    {
        await Dialog.Locator("span[role='combobox']").Last.ClickAsync();
        await page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });
        await page.Locator(".e-popup.e-ddl .e-list-item").Filter(new() { HasText = reasonLabel }).First.ClickAsync();
    }

    /// <summary>
    /// Fills the mode-specific adjustment value (Percentage / Fixed Amount / New Salary) — the
    /// only e-numerictextbox in the panel before a preview is built (the preview grid's per-row
    /// numeric boxes appear only after Build Preview). Types character-by-character since Syncfusion
    /// numeric inputs don't round-trip a bare FillAsync into the Blazor two-way binding.
    /// </summary>
    public async Task FillAdjustmentValueAsync(string value)
    {
        var input = Dialog.Locator("input.e-numerictextbox").First;
        await input.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.PressAsync("Delete");
        if (value.Length > 0)
            await input.PressSequentiallyAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillEffectiveDateAsync(string ddMMyyyy)
    {
        var input = Dialog.Locator(".e-date-wrapper input.e-input").First;
        await input.ClickAsync();
        await input.FillAsync(ddMMyyyy);
        await page.Keyboard.PressAsync("Tab");
    }

    public Task FillNotesAsync(string value) =>
        Dialog.Locator("textarea").FillAsync(value);

    public async Task ClickBuildPreviewAsync()
    {
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Build Preview", Exact = true }).ClickAsync();
        // The "Preview & Confirm" card only renders once _previewRows.Count > 0, or the
        // _globalError banner is set instead. page.WaitForSelectorAsync (unlike Locator.WaitForAsync)
        // tolerates a combined selector matching either outcome.
        await page.WaitForSelectorAsync(
            "[role='dialog'].bulk-compensation-update-dialog h5:has-text('Preview & Confirm'), " +
            "[role='dialog'].bulk-compensation-update-dialog .alert-danger",
            new() { Timeout = 15_000 });
    }

    // ── Preview & Confirm ────────────────────────────────────────────────────────

    private ILocator PreviewCard => Dialog.Locator(".card", new() { HasText = "Preview & Confirm" });

    public Task<bool> HasPreviewCardAsync() => PreviewCard.IsVisibleAsync();

    public async Task<int> GetPreviewRowCountAsync()
    {
        await page.WaitForSelectorAsync(
            "[role='dialog'].bulk-compensation-update-dialog .e-grid .e-row, " +
            "[role='dialog'].bulk-compensation-update-dialog .e-grid .e-emptyrow",
            new() { Timeout = 15_000 });
        return await PreviewCard.Locator(".e-grid .e-row").CountAsync();
    }

    public ILocator PreviewRow(string employeeNameFragment) =>
        PreviewCard.Locator(".e-grid .e-row").Filter(new() { HasText = employeeNameFragment });

    public async Task<decimal> GetProposedSalaryAsync(string employeeNameFragment)
    {
        var row = PreviewRow(employeeNameFragment).First;
        var value = await row.Locator("input.e-numerictextbox").First.InputValueAsync();
        return decimal.Parse(value);
    }

    public async Task<string?> GetExcludedEmployeesTextAsync()
    {
        var banner = Dialog.Locator(".alert-warning");
        return await banner.IsVisibleAsync() ? (await banner.TextContentAsync())?.Trim() : null;
    }

    /// <summary>
    /// Overwrites the Proposed Salary cell for the given row — the row's own SfNumericTextBox
    /// recalculates Difference/% Change client-side via ValueChanged, so the value must be typed
    /// character-by-character rather than set with a bare FillAsync, which never round-trips to
    /// the Blazor binding. Mirrors BulkCompensationUpdatePage.SetProposedSalaryAsync.
    /// </summary>
    public async Task SetProposedSalaryAsync(string employeeNameFragment, string value)
    {
        var row = PreviewRow(employeeNameFragment).First;
        var input = row.Locator("input.e-numerictextbox").First;
        await input.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.PressAsync("Delete");
        if (value.Length > 0)
            await input.PressSequentiallyAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    /// <summary>
    /// Clicks "Confirm Apply" and waits for the dialog to actually close. A successful apply
    /// round-trips through a real HTTP call (BulkApplyCompensationAdjustmentsAsync), bubbles
    /// OnApplied up through BulkCompensationUpdateDialog to EmployeeList (which sets its
    /// success banner and closes the dialog), and only then does the SfDialog detach from the
    /// DOM — so callers checking EmployeeListPage.GetActionSuccessMessageAsync() right after this
    /// returns need that detachment as the completion signal, since IsVisibleAsync() checks are
    /// an immediate snapshot, not an auto-waiting assertion, and would otherwise race the
    /// round-trip and see nothing yet.
    /// </summary>
    public async Task ConfirmApplyAsync()
    {
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Confirm Apply", Exact = true }).ClickAsync();
        await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }

    /// <summary>
    /// Returns the panel's own top-level error banner (_globalError inside
    /// BulkCompensationAdjustmentPanel), e.g. "Please enter an adjustment value." or "None of the
    /// selected employees have a current compensation record to adjust."
    /// </summary>
    public async Task<string?> GetGlobalErrorAsync()
    {
        var banner = Dialog.Locator(".alert-danger");
        return await banner.IsVisibleAsync() ? (await banner.TextContentAsync())?.Trim() : null;
    }

    public Task ClickCloseAsync() =>
        Dialog.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true }).ClickAsync();
}
