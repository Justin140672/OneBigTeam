using Microsoft.Playwright;
using HR.Web.E2E.Tests.Infrastructure;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the standalone HR Settings page (/companies/{id}/hr-settings), gated on
/// Session.IsHrAdministrator. Holds all the HR-policy fields that used to live on the Company
/// Settings tab (see HrSettingsPage.razor) — Working Week, Sickness, Document Acknowledgement,
/// Leaving Process, and Employee Numbering. Locator logic here is ported directly from
/// CompanyEditPage's now-removed equivalents since the underlying DOM markup is unchanged, just
/// relocated to a different page/route.
/// </summary>
public sealed class HrSettingsPage(IPage page, string baseUrl)
{
    private Guid _companyId;

    public async Task GoToAsync(Guid companyId)
    {
        _companyId = companyId;
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/hr-settings");
        await page.WaitForSelectorAsync(".card", new() { Timeout = 20_000 });
        // Wait for Syncfusion to initialise — span[role='combobox'] (the Leave Year Start
        // Month SfDropDownList) only appears after Blazor's interactive render completes.
        await page.WaitForSelectorAsync("span[role='combobox']", new() { Timeout = 20_000 });
    }

    // ── Working Week ─────────────────────────────────────────────────────────

    public async Task<bool> IsWorkingDayCheckedAsync(string dayName)
    {
        var label = page.Locator("label").Filter(new() { HasText = dayName }).First;
        var checkbox = label.Locator("input[type='checkbox']");
        return await checkbox.IsCheckedAsync();
    }

    public async Task SetWorkingDayAsync(string dayName, bool isChecked)
    {
        var current = await IsWorkingDayCheckedAsync(dayName);
        if (current != isChecked)
        {
            var label = page.Locator("label").Filter(new() { HasText = dayName }).First;
            await label.ClickAsync();
        }
    }

    // ── Numeric helpers (ported from CompanyEditPage) ───────────────────────────

    private ILocator NumericBoxByLabel(string columnClass, string labelText) =>
        page.Locator(columnClass).Filter(new() { HasText = labelText }).First.Locator("input").First;

    private async Task FillNumericAndVerifyAsync(ILocator input, string value, decimal expected, int maxAttempts = 3)
    {
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            await TypeIntoNumericInputAsync(input, value);

            var actual = await input.InputValueAsync();
            if (decimal.TryParse(actual, out var parsed) && parsed == expected)
                return;

            if (attempt < maxAttempts)
                await page.WaitForTimeoutAsync(200);
        }

        throw new PlaywrightException(
            $"Numeric input value did not stick after {maxAttempts} attempts: expected '{expected}', got '{await input.InputValueAsync()}'.");
    }

    private async Task FillNullableNumericAndVerifyAsync(ILocator input, int? value, int maxAttempts = 3)
    {
        var text = value?.ToString() ?? "";

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            await TypeIntoNumericInputAsync(input, text);

            var actual = await input.InputValueAsync();
            var actualParsed = int.TryParse(actual, out var parsed) ? parsed : (int?)null;
            if (actualParsed == value)
                return;

            if (attempt < maxAttempts)
                await page.WaitForTimeoutAsync(200);
        }

        throw new PlaywrightException(
            $"Nullable numeric input value did not stick after {maxAttempts} attempts: expected '{value?.ToString() ?? "(empty)"}', got '{await input.InputValueAsync()}'.");
    }

    private async Task TypeIntoNumericInputAsync(ILocator input, string text)
    {
        await input.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.PressAsync("Delete");
        if (text.Length > 0)
            await input.PressSequentiallyAsync(text);
        await page.Keyboard.PressAsync("Tab");
    }

    // ── Regional/core numeric + dropdown fields ─────────────────────────────────

    public async Task SetHoursPerDayAsync(decimal hours) =>
        await FillNumericAndVerifyAsync(NumericBoxByLabel(".col-md-3", "Hours Per Day"), hours.ToString("0.#"), hours);

    public async Task<decimal> GetHoursPerDayAsync()
    {
        var input = NumericBoxByLabel(".col-md-3", "Hours Per Day");
        var value = await input.InputValueAsync();
        return decimal.Parse(value);
    }

    public async Task SetDefaultHolidayAllowanceAsync(decimal days) =>
        await FillNumericAndVerifyAsync(NumericBoxByLabel(".col-md-3", "Default Holiday Allowance (days)"), days.ToString("0.#"), days);

    public async Task<decimal> GetDefaultHolidayAllowanceAsync()
    {
        var input = NumericBoxByLabel(".col-md-3", "Default Holiday Allowance (days)");
        var value = await input.InputValueAsync();
        return decimal.Parse(value);
    }

    public async Task SetProbationMonthsAsync(int months) =>
        await FillNumericAndVerifyAsync(NumericBoxByLabel(".col-md-3", "Probation Months"), months.ToString(), months);

    public async Task<int> GetProbationMonthsAsync()
    {
        var input = NumericBoxByLabel(".col-md-3", "Probation Months");
        var value = await input.InputValueAsync();
        return int.Parse(value);
    }

    public Task SelectLeaveYearStartMonthAsync(string monthName) =>
        DropDownSelector.SelectAsync(page, page.Locator(".col-md-3").Filter(new() { HasText = "Leave Year Start Month" }).First, monthName);

    public async Task<string> GetLeaveYearStartMonthAsync()
    {
        var group = page.Locator(".col-md-3")
            .Filter(new() { HasText = "Leave Year Start Month" })
            .First;
        var combobox = group.Locator("span[role='combobox']").First;
        return (await combobox.Locator("input").InputValueAsync()).Trim();
    }

    public async Task<bool> IsExcludePublicHolidaysFromSicknessCheckedAsync()
    {
        var wrapper = page.Locator(".e-checkbox-wrapper")
            .Filter(new() { HasText = "Exclude public holidays from sickness" });
        return await wrapper.Locator("input[type='checkbox']").IsCheckedAsync();
    }

    public async Task SetExcludePublicHolidaysFromSicknessAsync(bool isChecked)
    {
        var current = await IsExcludePublicHolidaysFromSicknessCheckedAsync();
        if (current != isChecked)
        {
            var wrapper = page.Locator(".e-checkbox-wrapper")
                .Filter(new() { HasText = "Exclude public holidays from sickness" });
            await wrapper.Locator("label").ClickAsync();
        }
    }

    public async Task<bool> IsExcludePublicHolidaysFromLeaveCheckedAsync()
    {
        var wrapper = page.Locator(".e-checkbox-wrapper")
            .Filter(new() { HasText = "Exclude public holidays from leave" });
        return await wrapper.Locator("input[type='checkbox']").IsCheckedAsync();
    }

    public async Task SetExcludePublicHolidaysFromLeaveAsync(bool isChecked)
    {
        var current = await IsExcludePublicHolidaysFromLeaveCheckedAsync();
        if (current != isChecked)
        {
            var wrapper = page.Locator(".e-checkbox-wrapper")
                .Filter(new() { HasText = "Exclude public holidays from leave" });
            await wrapper.Locator("label").ClickAsync();
        }
    }

    public async Task<bool> IsDisplaySalaryOnEmployeeProfileCheckedAsync()
    {
        var wrapper = page.Locator(".e-checkbox-wrapper")
            .Filter(new() { HasText = "Display salary to employees on their profile" });
        return await wrapper.Locator("input[type='checkbox']").IsCheckedAsync();
    }

    public async Task SetDisplaySalaryOnEmployeeProfileAsync(bool isChecked)
    {
        var current = await IsDisplaySalaryOnEmployeeProfileCheckedAsync();
        if (current != isChecked)
        {
            var wrapper = page.Locator(".e-checkbox-wrapper")
                .Filter(new() { HasText = "Display salary to employees on their profile" });
            await wrapper.Locator("label").ClickAsync();
        }
    }

    public async Task SetFitNoteRequiredAfterDaysAsync(int? days)
    {
        await SwitchToTabAsync("Sickness");
        await FillNullableNumericAndVerifyAsync(NumericBoxByLabel(".col-md-4", "Fit Note Required After (Days)"), days);
    }

    public async Task<int?> GetFitNoteRequiredAfterDaysAsync()
    {
        await SwitchToTabAsync("Sickness");
        var input = NumericBoxByLabel(".col-md-4", "Fit Note Required After (Days)");
        var value = await input.InputValueAsync();
        return int.TryParse(value, out var parsed) ? parsed : null;
    }

    public async Task SetReturnToWorkRequiredAfterDaysAsync(int? days)
    {
        await SwitchToTabAsync("Sickness");
        await FillNullableNumericAndVerifyAsync(NumericBoxByLabel(".col-md-4", "Return-to-Work Review Required After (Days)"), days);
    }

    public async Task<int?> GetReturnToWorkRequiredAfterDaysAsync()
    {
        await SwitchToTabAsync("Sickness");
        var input = NumericBoxByLabel(".col-md-4", "Return-to-Work Review Required After (Days)");
        var value = await input.InputValueAsync();
        return int.TryParse(value, out var parsed) ? parsed : null;
    }

    // ── Document Acknowledgement ─────────────────────────────────────────────────

    private ILocator DefaultAcknowledgementStatementTextArea =>
        page.GetByPlaceholder("I confirm that I have read and understood this document.");

    public async Task SetDefaultAcknowledgementStatementAsync(string value)
    {
        await SwitchToTabAsync("Document Acknowledgement");
        await DefaultAcknowledgementStatementTextArea.FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task<string> GetDefaultAcknowledgementStatementAsync()
    {
        await SwitchToTabAsync("Document Acknowledgement");
        return await DefaultAcknowledgementStatementTextArea.InputValueAsync();
    }

    public async Task SetAcknowledgementReminderIntervalDaysAsync(int days)
    {
        await SwitchToTabAsync("Document Acknowledgement");
        await FillNumericAndVerifyAsync(NumericBoxByLabel(".col-md-4", "Acknowledgement Reminder Interval (days)"), days.ToString(), days);
    }

    public async Task<int> GetAcknowledgementReminderIntervalDaysAsync()
    {
        await SwitchToTabAsync("Document Acknowledgement");
        var input = NumericBoxByLabel(".col-md-4", "Acknowledgement Reminder Interval (days)");
        var value = await input.InputValueAsync();
        return int.Parse(value);
    }

    // ── Leaving Process / Notice Period ──────────────────────────────────────────

    public async Task SelectNoticePeriodPresetAsync(string presetLabel)
    {
        await SwitchToTabAsync("Leaving Process");
        await DropDownSelector.SelectAsync(page, page.Locator(".col-md-4").Filter(new() { HasText = "Default Notice Period" }).First, presetLabel);
    }

    public async Task<string> GetNoticePeriodPresetAsync()
    {
        await SwitchToTabAsync("Leaving Process");
        var group = page.Locator(".col-md-4")
            .Filter(new() { HasText = "Default Notice Period" })
            .First;
        var combobox = group.Locator("span[role='combobox']").First;
        return (await combobox.Locator("input").InputValueAsync()).Trim();
    }

    public async Task WaitForNoticePeriodCustomControlsAsync()
    {
        await SwitchToTabAsync("Leaving Process");
        await page.Locator(".col-md-4").Filter(new() { HasText = "Unit" }).First
            .WaitForAsync(new() { Timeout = 10_000 });
    }

    public async Task SelectNoticePeriodUnitAsync(string unitLabel)
    {
        await SwitchToTabAsync("Leaving Process");
        await DropDownSelector.SelectAsync(page, page.Locator(".col-md-4").Filter(new() { HasText = "Unit" }).First, unitLabel);
    }

    public async Task<string> GetNoticePeriodUnitAsync()
    {
        await SwitchToTabAsync("Leaving Process");
        var group = page.Locator(".col-md-4")
            .Filter(new() { HasText = "Unit" })
            .First;
        var combobox = group.Locator("span[role='combobox']").First;
        return (await combobox.Locator("input").InputValueAsync()).Trim();
    }

    public async Task SetNoticePeriodLengthAsync(int length)
    {
        await SwitchToTabAsync("Leaving Process");
        await FillNumericAndVerifyAsync(NumericBoxByLabel(".col-md-4", "Length"), length.ToString(), length);
    }

    public async Task<int> GetNoticePeriodLengthAsync()
    {
        await SwitchToTabAsync("Leaving Process");
        var input = NumericBoxByLabel(".col-md-4", "Length");
        var value = await input.InputValueAsync();
        return int.Parse(value);
    }

    public async Task<bool> IsAutoDisableAccessOnLeavingDateCheckedAsync()
    {
        await SwitchToTabAsync("Leaving Process");
        var wrapper = page.Locator(".e-checkbox-wrapper")
            .Filter(new() { HasText = "Automatically disable system access on the employee's leaving date" });
        return await wrapper.Locator("input[type='checkbox']").IsCheckedAsync();
    }

    public async Task SetAutoDisableAccessOnLeavingDateAsync(bool isChecked)
    {
        var current = await IsAutoDisableAccessOnLeavingDateCheckedAsync();
        if (current != isChecked)
        {
            var wrapper = page.Locator(".e-checkbox-wrapper")
                .Filter(new() { HasText = "Automatically disable system access on the employee's leaving date" });
            await wrapper.Locator("label").ClickAsync();
        }
    }

    // ── Employee Numbering ────────────────────────────────────────────────────

    // HrSettingsPage.razor groups its fields into an SfTab (Working & Leave, Sickness,
    // Document Acknowledgement, Leaving Process, Employee Numbering, Asset Numbering) instead of
    // one flat card — GoToAsync always lands on the first tab, and SfTab only renders the active
    // tab's content, so every accessor for a field on a non-first tab must activate that tab first
    // (the same way MyProfilePage.OpenTasksTabAsync does for its tabs).
    private Task SwitchToTabAsync(string tabName) =>
        // Not Exact: SfTab headers can carry an error-icon span that perturbs the accessible name.
        page.GetByRole(AriaRole.Tab, new() { Name = tabName }).First.ClickAsync();

    private Task SwitchToEmployeeNumberingTabAsync() => SwitchToTabAsync("Employee Numbering");

    public async Task SelectEmployeeNumberModeAsync(string modeLabel)
    {
        await SwitchToEmployeeNumberingTabAsync();
        await DropDownSelector.SelectAsync(page, page.Locator(".col-md-4").Filter(new() { HasText = "Numbering Mode" }).First, modeLabel);
    }

    public async Task<string> GetEmployeeNumberModeAsync()
    {
        await SwitchToEmployeeNumberingTabAsync();
        var group = page.Locator(".col-md-4")
            .Filter(new() { HasText = "Numbering Mode" })
            .First;
        var combobox = group.Locator("span[role='combobox']").First;
        return (await combobox.Locator("input").InputValueAsync()).Trim();
    }

    public async Task<bool> IsEmployeeNumberAutomaticFieldsVisibleAsync()
    {
        await SwitchToEmployeeNumberingTabAsync();
        return await page.GetByPlaceholder("e.g. EMP-").IsVisibleAsync();
    }

    public async Task SetEmployeeNumberPrefixAsync(string value)
    {
        await SwitchToEmployeeNumberingTabAsync();
        await page.GetByPlaceholder("e.g. EMP-").FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task<string> GetEmployeeNumberPrefixAsync()
    {
        await SwitchToEmployeeNumberingTabAsync();
        return await page.GetByPlaceholder("e.g. EMP-").InputValueAsync();
    }

    public async Task SetNextEmployeeNumberAsync(int value)
    {
        await SwitchToEmployeeNumberingTabAsync();
        await FillNumericAndVerifyAsync(NumericBoxByLabel(".col-md-4", "Next Number"), value.ToString(), value);
    }

    public async Task<int> GetNextEmployeeNumberAsync()
    {
        await SwitchToEmployeeNumberingTabAsync();
        var input = NumericBoxByLabel(".col-md-4", "Next Number");
        var value = await input.InputValueAsync();
        return int.Parse(value);
    }

    public async Task SetEmployeeNumberMinimumLengthAsync(int value)
    {
        await SwitchToEmployeeNumberingTabAsync();
        await FillNumericAndVerifyAsync(NumericBoxByLabel(".col-md-4", "Minimum Numeric Length"), value.ToString(), value);
    }

    public async Task<int> GetEmployeeNumberMinimumLengthAsync()
    {
        await SwitchToEmployeeNumberingTabAsync();
        var input = NumericBoxByLabel(".col-md-4", "Minimum Numeric Length");
        var value = await input.InputValueAsync();
        return int.Parse(value);
    }

    public async Task<string?> GetEmployeeNumberPreviewAsync()
    {
        await SwitchToEmployeeNumberingTabAsync();
        var paragraph = page.Locator("p").Filter(new() { HasText = "Preview:" }).First;
        if (!await paragraph.IsVisibleAsync())
            return null;

        // The preview text is recomputed by Blazor after the numeric fields' OnChange/blur
        // handlers fire; give it a moment to re-render before reading the DOM.
        await page.WaitForTimeoutAsync(200);
        return (await paragraph.TextContentAsync())?.Trim();
    }

    // ── Renumber-existing-employees confirmation ─────────────────────────────────
    // Changing the employee-number prefix or minimum length WHILE the company is in Automatic mode
    // pops this confirmation before the save proceeds (HrSettingsPage.razor's _showRenumberWarning
    // SfDialog); confirming it queues the background renumber of every existing employee.

    private ILocator RenumberDialog =>
        page.Locator("[role='dialog']").Filter(new() { HasText = "Renumber existing employees?" });

    public async Task<bool> IsRenumberDialogVisibleAsync()
    {
        try
        {
            await RenumberDialog.First.WaitForAsync(
                new() { State = WaitForSelectorState.Visible, Timeout = 4_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task ConfirmRenumberAsync()
    {
        await RenumberDialog.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();
        await RenumberDialog.First.WaitForAsync(
            new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    public async Task CancelRenumberAsync()
    {
        await RenumberDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        await RenumberDialog.First.WaitForAsync(
            new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    // ── Save / Cancel ────────────────────────────────────────────────────────

    public Task ClickSaveAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();

    public async Task SaveAsync()
    {
        await ClickSaveAsync();
        // A prefix / minimum-length change in Automatic mode interposes the renumber confirmation
        // before the save runs — confirm it and let the save through. Short settle (the dialog is
        // one Blazor Server round-trip away) rather than a long wait on every save.
        await page.WaitForTimeoutAsync(600);
        if (await RenumberDialog.First.IsVisibleAsync())
            await ConfirmRenumberAsync();
        await page.WaitForSpinnerToClearAsync();

        // A SUCCESSFUL save navigates away to the HR dashboard (HrSettingsPage.razor's
        // ListUrl => "/dashboard/hr", via EditPageBase.OnSavedAsync). A FAILED save (validation
        // error) stays put. Re-open the settings page after a successful save so post-save
        // accessors/assertions (e.g. GetEmployeeNumberModeAsync, reload-persistence checks) keep
        // working against the settings form rather than the dashboard.
        await page.WaitForTimeoutAsync(300);
        if (!page.Url.Contains("/hr-settings", StringComparison.OrdinalIgnoreCase))
            await GoToAsync(_companyId);
    }

    public Task CancelAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();

    public async Task<bool> HasErrorAsync()
    {
        try
        {
            await page.Locator(".alert-danger, .validation-message").First.WaitForAsync(new() { Timeout = 5_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    // ── Section headings (presence-only rendering checks) ───────────────────────

    public Task<bool> IsWorkingWeekSectionVisibleAsync() =>
        page.GetByText("Working Week", new() { Exact = true }).IsVisibleAsync();

    public Task<bool> IsSicknessSectionVisibleAsync() =>
        page.GetByText("Sickness", new() { Exact = true }).IsVisibleAsync();

    public Task<bool> IsDocumentAcknowledgementSectionVisibleAsync() =>
        page.GetByText("Document Acknowledgement", new() { Exact = true }).IsVisibleAsync();

    public Task<bool> IsLeavingProcessSectionVisibleAsync() =>
        page.GetByText("Leaving Process", new() { Exact = true }).IsVisibleAsync();

    public Task<bool> IsEmployeeNumberingSectionVisibleAsync() =>
        page.GetByText("Employee Numbering", new() { Exact = true }).IsVisibleAsync();
}
