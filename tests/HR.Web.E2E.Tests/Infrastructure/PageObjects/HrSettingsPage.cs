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
    public async Task GoToAsync(Guid companyId)
    {
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

    public async Task SetFitNoteRequiredAfterDaysAsync(int? days) =>
        await FillNullableNumericAndVerifyAsync(NumericBoxByLabel(".col-md-4", "Fit Note Required After (Days)"), days);

    public async Task<int?> GetFitNoteRequiredAfterDaysAsync()
    {
        var input = NumericBoxByLabel(".col-md-4", "Fit Note Required After (Days)");
        var value = await input.InputValueAsync();
        return int.TryParse(value, out var parsed) ? parsed : null;
    }

    public async Task SetReturnToWorkRequiredAfterDaysAsync(int? days) =>
        await FillNullableNumericAndVerifyAsync(NumericBoxByLabel(".col-md-4", "Return-to-Work Review Required After (Days)"), days);

    public async Task<int?> GetReturnToWorkRequiredAfterDaysAsync()
    {
        var input = NumericBoxByLabel(".col-md-4", "Return-to-Work Review Required After (Days)");
        var value = await input.InputValueAsync();
        return int.TryParse(value, out var parsed) ? parsed : null;
    }

    // ── Document Acknowledgement ─────────────────────────────────────────────────

    private ILocator DefaultAcknowledgementStatementTextArea =>
        page.GetByPlaceholder("I confirm that I have read and understood this document.");

    public Task SetDefaultAcknowledgementStatementAsync(string value) =>
        DefaultAcknowledgementStatementTextArea.FillAsync(value);

    public Task<string> GetDefaultAcknowledgementStatementAsync() =>
        DefaultAcknowledgementStatementTextArea.InputValueAsync();

    public async Task SetAcknowledgementReminderIntervalDaysAsync(int days) =>
        await FillNumericAndVerifyAsync(NumericBoxByLabel(".col-md-4", "Acknowledgement Reminder Interval (days)"), days.ToString(), days);

    public async Task<int> GetAcknowledgementReminderIntervalDaysAsync()
    {
        var input = NumericBoxByLabel(".col-md-4", "Acknowledgement Reminder Interval (days)");
        var value = await input.InputValueAsync();
        return int.Parse(value);
    }

    // ── Leaving Process / Notice Period ──────────────────────────────────────────

    public Task SelectNoticePeriodPresetAsync(string presetLabel) =>
        DropDownSelector.SelectAsync(page, page.Locator(".col-md-4").Filter(new() { HasText = "Default Notice Period" }).First, presetLabel);

    public async Task<string> GetNoticePeriodPresetAsync()
    {
        var group = page.Locator(".col-md-4")
            .Filter(new() { HasText = "Default Notice Period" })
            .First;
        var combobox = group.Locator("span[role='combobox']").First;
        return (await combobox.Locator("input").InputValueAsync()).Trim();
    }

    public Task WaitForNoticePeriodCustomControlsAsync() =>
        page.Locator(".col-md-4").Filter(new() { HasText = "Unit" }).First
            .WaitForAsync(new() { Timeout = 10_000 });

    public Task SelectNoticePeriodUnitAsync(string unitLabel) =>
        DropDownSelector.SelectAsync(page, page.Locator(".col-md-4").Filter(new() { HasText = "Unit" }).First, unitLabel);

    public async Task<string> GetNoticePeriodUnitAsync()
    {
        var group = page.Locator(".col-md-4")
            .Filter(new() { HasText = "Unit" })
            .First;
        var combobox = group.Locator("span[role='combobox']").First;
        return (await combobox.Locator("input").InputValueAsync()).Trim();
    }

    public async Task SetNoticePeriodLengthAsync(int length) =>
        await FillNumericAndVerifyAsync(NumericBoxByLabel(".col-md-4", "Length"), length.ToString(), length);

    public async Task<int> GetNoticePeriodLengthAsync()
    {
        var input = NumericBoxByLabel(".col-md-4", "Length");
        var value = await input.InputValueAsync();
        return int.Parse(value);
    }

    public async Task<bool> IsAutoDisableAccessOnLeavingDateCheckedAsync()
    {
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

    public Task SelectEmployeeNumberModeAsync(string modeLabel) =>
        DropDownSelector.SelectAsync(page, page.Locator(".col-md-4").Filter(new() { HasText = "Numbering Mode" }).First, modeLabel);

    public async Task<string> GetEmployeeNumberModeAsync()
    {
        var group = page.Locator(".col-md-4")
            .Filter(new() { HasText = "Numbering Mode" })
            .First;
        var combobox = group.Locator("span[role='combobox']").First;
        return (await combobox.Locator("input").InputValueAsync()).Trim();
    }

    public Task<bool> IsEmployeeNumberAutomaticFieldsVisibleAsync() =>
        page.GetByPlaceholder("e.g. EMP-").IsVisibleAsync();

    public Task SetEmployeeNumberPrefixAsync(string value) =>
        page.GetByPlaceholder("e.g. EMP-").FillAsync(value);

    public Task<string> GetEmployeeNumberPrefixAsync() =>
        page.GetByPlaceholder("e.g. EMP-").InputValueAsync();

    public Task SetNextEmployeeNumberAsync(int value) =>
        FillNumericAndVerifyAsync(NumericBoxByLabel(".col-md-4", "Next Number"), value.ToString(), value);

    public async Task<int> GetNextEmployeeNumberAsync()
    {
        var input = NumericBoxByLabel(".col-md-4", "Next Number");
        var value = await input.InputValueAsync();
        return int.Parse(value);
    }

    public Task SetEmployeeNumberMinimumLengthAsync(int value) =>
        FillNumericAndVerifyAsync(NumericBoxByLabel(".col-md-4", "Minimum Numeric Length"), value.ToString(), value);

    public async Task<int> GetEmployeeNumberMinimumLengthAsync()
    {
        var input = NumericBoxByLabel(".col-md-4", "Minimum Numeric Length");
        var value = await input.InputValueAsync();
        return int.Parse(value);
    }

    public async Task<string?> GetEmployeeNumberPreviewAsync()
    {
        var paragraph = page.Locator("p").Filter(new() { HasText = "Preview:" }).First;
        return await paragraph.IsVisibleAsync() ? (await paragraph.TextContentAsync())?.Trim() : null;
    }

    private ILocator BackfillEmployeeNumbersButton =>
        page.GetByRole(AriaRole.Button, new() { Name = "Backfill Employee Numbers…" });

    public Task<bool> IsBackfillEmployeeNumbersButtonVisibleAsync() =>
        BackfillEmployeeNumbersButton.IsVisibleAsync();

    public Task OpenBackfillEmployeeNumbersDialogAsync() =>
        BackfillEmployeeNumbersButton.ClickAsync();

    // ── Save / Cancel ────────────────────────────────────────────────────────

    public async Task SaveAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await page.WaitForSpinnerToClearAsync();
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
