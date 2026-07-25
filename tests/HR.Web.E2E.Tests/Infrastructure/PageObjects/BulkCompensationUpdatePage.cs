using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the Bulk Compensation Update page
/// (Components/Pages/Employees/BulkCompensationUpdate.razor):
/// select employees -&gt; choose an adjustment mode -&gt; build/edit a preview -&gt; confirm apply,
/// plus the standalone "Download Import Template" / "Import from Excel" panel.
/// </summary>
public sealed class BulkCompensationUpdatePage(IPage page, string baseUrl)
{
    // ── Navigation ──────────────────────────────────────────────────────────────

    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/employees/compensation/bulk-update");
        // The employee picker list only renders once LoadEmployeesAsync completes; the search
        // box is present from first render, so wait on it as the interactive-render signal.
        await page.WaitForSelectorAsync(".employee-picker-list, .card:has-text('1. Select Employees')",
            new() { Timeout = 20_000 });
    }

    // ── 1. Select Employees ─────────────────────────────────────────────────────

    public Task SearchEmployeeAsync(string text) =>
        page.GetByPlaceholder("Search by name or email").FillAsync(text);

    /// <summary>
    /// Checks the employee whose row label contains the given text fragment (e.g. a unique last
    /// name). Scoped to ".employee-picker-list .form-check" — clicking the label (rather than the
    /// checkbox input directly) mirrors a real user interaction and avoids any ambiguity between
    /// the input and its "for"-linked label.
    /// </summary>
    public async Task SelectEmployeeAsync(string nameFragment)
    {
        var row = page.Locator(".employee-picker-list .form-check").Filter(new() { HasText = nameFragment }).First;
        await row.Locator("label.form-check-label").ClickAsync();
    }

    public Task SelectAllFilteredAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "Select All Filtered" }).ClickAsync();

    public Task ClearSelectionAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "Clear Selection" }).ClickAsync();

    public async Task<int> GetSelectedCountAsync()
    {
        var text = await page.Locator(".card:has-text('1. Select Employees') p.text-muted").InnerTextAsync();
        var digits = new string(text.TakeWhile(char.IsDigit).ToArray());
        return int.Parse(digits.Length > 0 ? digits : "0");
    }

    // ── 2. Adjustment Details ────────────────────────────────────────────────────

    private ILocator AdjustmentCard => page.Locator(".card", new() { HasText = "2. Adjustment Details" });

    /// <summary>
    /// Selects the Adjustment Mode dropdown (the first of two SfDropDownLists in this card — the
    /// second is Reason). Value must match one of the ModeOptions labels exactly, e.g.
    /// "Percentage Increase", "Fixed Amount Increase", "Set Salary Directly".
    /// </summary>
    public async Task SelectModeAsync(string modeLabel)
    {
        await AdjustmentCard.Locator("span[role='combobox']").First.ClickAsync();
        await page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });
        await page.Locator(".e-popup.e-ddl .e-list-item").Filter(new() { HasText = modeLabel }).First.ClickAsync();
    }

    /// <summary>
    /// Selects the Reason dropdown (the second of two SfDropDownLists in this card).
    /// </summary>
    public async Task SelectReasonAsync(string reasonLabel)
    {
        await AdjustmentCard.Locator("span[role='combobox']").Last.ClickAsync();
        await page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });
        await page.Locator(".e-popup.e-ddl .e-list-item").Filter(new() { HasText = reasonLabel }).First.ClickAsync();
    }

    /// <summary>
    /// Fills the mode-specific adjustment value (Percentage / Fixed Amount / New Salary) — the
    /// only e-numerictextbox in this card (the preview grid's per-row numeric boxes live in a
    /// different card below, rendered only after Build Preview). Types each character for real
    /// (see EmployeeEditPage's equivalent helper) rather than a bare FillAsync, which bypasses the
    /// Blazor two-way-bound value entirely for Syncfusion numeric inputs.
    /// </summary>
    public async Task FillAdjustmentValueAsync(string value)
    {
        var input = AdjustmentCard.Locator("input.e-numerictextbox").First;
        await input.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.PressAsync("Delete");
        if (value.Length > 0)
            await input.PressSequentiallyAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillEffectiveDateAsync(string ddMMyyyy)
    {
        var input = AdjustmentCard.Locator(".e-date-wrapper input.e-input").First;
        await input.ClickAsync();
        await input.FillAsync(ddMMyyyy);
        await page.Keyboard.PressAsync("Tab");
    }

    public Task FillNotesAsync(string value) =>
        AdjustmentCard.Locator("textarea").FillAsync(value);

    public async Task ClickBuildPreviewAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Build Preview", Exact = true }).ClickAsync();
        // The "3. Preview & Confirm" card only renders once _previewRows.Count > 0, or the
        // _globalError banner is set instead (e.g. "None of the selected employees have a current
        // compensation record…"). page.WaitForSelectorAsync (the page-level API, unlike
        // Locator.WaitForAsync) tolerates a combined selector matching either outcome — it just
        // waits for at least one to appear, so no Task.WhenAny/unobserved-task juggling is needed.
        await page.WaitForSelectorAsync(
            "h5:has-text('3. Preview & Confirm'), .container-fluid > .alert-danger",
            new() { Timeout = 15_000 });
    }

    // ── 3. Preview & Confirm ─────────────────────────────────────────────────────

    private ILocator PreviewCard => page.Locator(".card", new() { HasText = "3. Preview & Confirm" });

    public Task<bool> HasPreviewCardAsync() =>
        PreviewCard.IsVisibleAsync();

    public async Task<int> GetPreviewRowCountAsync()
    {
        await page.WaitForSelectorAsync(".e-grid .e-row, .e-grid .e-emptyrow", new() { Timeout = 15_000 });
        return await PreviewCard.Locator(".e-grid .e-row").CountAsync();
    }

    public async Task<string?> GetExcludedEmployeesTextAsync()
    {
        var banner = page.Locator(".alert-warning");
        return await banner.IsVisibleAsync() ? (await banner.TextContentAsync())?.Trim() : null;
    }

    public ILocator PreviewRow(string employeeNameFragment) =>
        PreviewCard.Locator(".e-grid .e-row").Filter(new() { HasText = employeeNameFragment });

    public async Task<decimal> GetProposedSalaryAsync(string employeeNameFragment)
    {
        var row = PreviewRow(employeeNameFragment).First;
        var value = await row.Locator("input.e-numerictextbox").First.InputValueAsync();
        return decimal.Parse(value);
    }

    /// <summary>
    /// Overwrites the Proposed Salary cell for the given row — the row's own SfNumericTextBox
    /// recalculates Difference/% Change client-side via ValueChanged, so (as with
    /// EmployeeEditPage's numeric-input helpers) the value must be typed character-by-character
    /// rather than set with a bare FillAsync, which never round-trips to the Blazor binding.
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

    public async Task ConfirmApplyAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Confirm Apply", Exact = true }).ClickAsync();
        // Applying is a single round-trip that either clears the preview grid and shows a
        // top-level success banner, or leaves the preview up with a top-level error banner.
        await page.WaitForSelectorAsync(
            ".container-fluid > .alert-success, .container-fluid > .alert-danger",
            new() { Timeout = 15_000 });
    }

    // ── Top-level banners ────────────────────────────────────────────────────────

    /// <summary>
    /// The page's top-level success banner (_successMessage), scoped to a direct child of
    /// ".container-fluid" so it can't be confused with the Import panel's own nested
    /// ".alert-success" (_importResult), which lives inside the "Import from Excel" card.
    /// </summary>
    public async Task<string?> GetSuccessMessageAsync()
    {
        var banner = page.Locator(".container-fluid > .alert-success");
        return await banner.IsVisibleAsync() ? (await banner.TextContentAsync())?.Trim() : null;
    }

    /// <summary>
    /// The page's top-level error banner (_globalError), scoped the same way as
    /// <see cref="GetSuccessMessageAsync"/> to avoid the Import panel's nested row-errors alert.
    /// </summary>
    public async Task<string?> GetGlobalErrorAsync()
    {
        var banner = page.Locator(".container-fluid > .alert-danger");
        return await banner.IsVisibleAsync() ? (await banner.TextContentAsync())?.Trim() : null;
    }

    // ── Download Template ────────────────────────────────────────────────────────

    public async Task<string> ClickDownloadTemplateAsync()
    {
        var downloadTask = page.WaitForDownloadAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Download Import Template" }).ClickAsync();
        var download = await downloadTask;
        return download.SuggestedFilename;
    }

    // ── Import from Excel ────────────────────────────────────────────────────────

    public Task UploadImportFileAsync(string filePath) =>
        page.Locator("input[type='file']").SetInputFilesAsync(filePath);

    public async Task ClickImportAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Import from Excel", Exact = true }).ClickAsync();
        // Either a nested success alert (_importResult) or a nested row-errors alert
        // (_importRowErrors) appears inside the "Import from Excel" card.
        await page.WaitForSelectorAsync(
            ".card:has-text('Import from Excel') .alert-success, .card:has-text('Import from Excel') .alert-danger",
            new() { Timeout = 15_000 });
    }

    public async Task<string?> GetImportSuccessMessageAsync()
    {
        var banner = page.Locator(".card", new() { HasText = "Import from Excel" }).Locator(".alert-success");
        return await banner.IsVisibleAsync() ? (await banner.TextContentAsync())?.Trim() : null;
    }

    public async Task<string?> GetImportRowErrorsTextAsync()
    {
        var banner = page.Locator(".card", new() { HasText = "Import from Excel" }).Locator(".alert-danger");
        return await banner.IsVisibleAsync() ? (await banner.TextContentAsync())?.Trim() : null;
    }
}
