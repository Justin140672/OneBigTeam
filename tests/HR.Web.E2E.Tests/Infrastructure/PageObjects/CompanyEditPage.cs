using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the company edit page (/companies/{id}/edit).
/// Covers the Profile and Settings tabs.
/// </summary>
public sealed class CompanyEditPage(IPage page, string baseUrl)
{
    public async Task GoToAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/edit");
        await page.WaitForSelectorAsync("[role='tablist']", new() { Timeout = 20_000 });
    }

    // ── Tab navigation ─────────────────────────────────────────────────────────

    public async Task OpenProfileTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Profile" }).ClickAsync();
        await page.WaitForSelectorAsync(".card", new() { Timeout = 15_000 });
    }

    public async Task OpenSettingsTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Settings" }).ClickAsync();
        await page.WaitForSelectorAsync(".card", new() { Timeout = 15_000 });
        // Wait for Syncfusion to initialise — span[role='combobox'] (the Leave Year Start
        // Month SfDropDownList) only appears after Blazor's interactive render completes.
        await page.WaitForSelectorAsync("span[role='combobox']", new() { Timeout = 20_000 });
    }

    public async Task OpenAddressesTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Addresses" }).ClickAsync();
        await page.WaitForSelectorAsync(".card", new() { Timeout = 15_000 });
    }

    // ── Profile tab ────────────────────────────────────────────────────────────

    /// <summary>Returns the company name shown in the h1 heading.</summary>
    public async Task<string> GetCompanyNameAsync() =>
        (await page.Locator("h1").TextContentAsync())?.Trim() ?? "";

    public async Task<bool> IsActiveAsync() =>
        await page.Locator("h1 ~ .badge.bg-success, .badge.bg-success").First.IsVisibleAsync();

    // ── Settings tab ───────────────────────────────────────────────────────────

    /// <summary>Returns true if a working-week day checkbox (e.g. "Monday") is currently checked.</summary>
    public async Task<bool> IsWorkingDayCheckedAsync(string dayName)
    {
        var label = page.Locator("label").Filter(new() { HasText = dayName }).First;
        // The associated checkbox precedes the label in the working-week group.
        var checkbox = label.Locator("input[type='checkbox']");
        return await checkbox.IsCheckedAsync();
    }

    /// <summary>
    /// Sets a working-week day checkbox (e.g. "Monday") to the desired checked state.
    /// Only clicks if the current state differs from the requested one.
    /// </summary>
    public async Task SetWorkingDayAsync(string dayName, bool isChecked)
    {
        var current = await IsWorkingDayCheckedAsync(dayName);
        if (current != isChecked)
        {
            var label = page.Locator("label").Filter(new() { HasText = dayName }).First;
            await label.ClickAsync();
        }
    }

    /// <summary>
    /// Locates a Syncfusion SfNumericTextBox (FloatLabelType.Auto) by scoping to its
    /// containing column and filtering by the floating label text — it renders the
    /// Placeholder prop as a floating label, not a native HTML placeholder attribute,
    /// so GetByPlaceholder doesn't match it (unlike plain HTML inputs elsewhere in this app).
    /// </summary>
    private ILocator NumericBoxByLabel(string columnClass, string labelText) =>
        page.Locator(columnClass).Filter(new() { HasText = labelText }).First.Locator("input").First;

    /// <summary>
    /// Fills a numeric input and confirms the parsed value actually stuck before returning,
    /// retrying if not — Syncfusion's SfNumericTextBox reformats the displayed value (e.g.
    /// "28" becomes "28.00"), so a strict string comparison would always mismatch, and a
    /// bare "fire and forget" fill can race with Blazor's server round-trip for the
    /// two-way bound value (observed with DefaultHolidayAllowance reverting to its seeded
    /// default after save+reload).
    /// </summary>
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

    /// <summary>Nullable-int variant of <see cref="FillNumericAndVerifyAsync"/> — null clears the field.</summary>
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

    /// <summary>
    /// Types into a Syncfusion SfNumericTextBox via real keystrokes instead of FillAsync.
    /// FillAsync sets the underlying DOM value through CDP directly, which bypasses the
    /// component's own JS keyup/input listeners that sync the typed value back to the
    /// Blazor-bound model — so a value that visually "fills" never actually round-trips
    /// to the server. Click-to-focus, select-all, delete, then type each character.
    /// </summary>
    private async Task TypeIntoNumericInputAsync(ILocator input, string text)
    {
        await input.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.PressAsync("Delete");
        if (text.Length > 0)
            await input.PressSequentiallyAsync(text);
        await page.Keyboard.PressAsync("Tab");
    }

    /// <summary>Sets the "Hours Per Day" numeric field.</summary>
    public async Task SetHoursPerDayAsync(decimal hours) =>
        await FillNumericAndVerifyAsync(NumericBoxByLabel(".col-md-3", "Hours Per Day"), hours.ToString("0.#"), hours);

    /// <summary>Returns the current value of the "Hours Per Day" numeric field.</summary>
    public async Task<decimal> GetHoursPerDayAsync()
    {
        var input = NumericBoxByLabel(".col-md-3", "Hours Per Day");
        var value = await input.InputValueAsync();
        return decimal.Parse(value);
    }

    /// <summary>Sets the "Default Holiday Allowance (days)" numeric field.</summary>
    public async Task SetDefaultHolidayAllowanceAsync(decimal days) =>
        await FillNumericAndVerifyAsync(NumericBoxByLabel(".col-md-3", "Default Holiday Allowance (days)"), days.ToString("0.#"), days);

    /// <summary>Returns the current value of the "Default Holiday Allowance (days)" numeric field.</summary>
    public async Task<decimal> GetDefaultHolidayAllowanceAsync()
    {
        var input = NumericBoxByLabel(".col-md-3", "Default Holiday Allowance (days)");
        var value = await input.InputValueAsync();
        return decimal.Parse(value);
    }

    /// <summary>Sets the "Probation Months" numeric field.</summary>
    public async Task SetProbationMonthsAsync(int months) =>
        await FillNumericAndVerifyAsync(NumericBoxByLabel(".col-md-3", "Probation Months"), months.ToString(), months);

    /// <summary>Returns the current value of the "Probation Months" numeric field.</summary>
    public async Task<int> GetProbationMonthsAsync()
    {
        var input = NumericBoxByLabel(".col-md-3", "Probation Months");
        var value = await input.InputValueAsync();
        return int.Parse(value);
    }

    /// <summary>
    /// Selects a value from the "Leave Year Start Month" dropdown.
    /// </summary>
    public async Task SelectLeaveYearStartMonthAsync(string monthName)
    {
        var group = page.Locator(".col-md-3")
            .Filter(new() { HasText = "Leave Year Start Month" })
            .First;
        await group.Locator("span[role='combobox']").First.ClickAsync();
        await page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });
        await page.Locator(".e-popup.e-ddl .e-list-item")
            .Filter(new() { HasText = monthName })
            .First
            .ClickAsync();

        // The popup (and its full-viewport click-away layer) can take a moment to detach after
        // selection — without waiting for it, a later click elsewhere on the page (e.g. Save)
        // can get blocked by the still-present overlay and time out looking "not found".
        await page.WaitForSelectorAsync(".e-popup.e-ddl", new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    /// <summary>Returns the currently displayed value of the "Leave Year Start Month" dropdown.</summary>
    public async Task<string> GetLeaveYearStartMonthAsync()
    {
        var group = page.Locator(".col-md-3")
            .Filter(new() { HasText = "Leave Year Start Month" })
            .First;
        var combobox = group.Locator("span[role='combobox']").First;
        return (await combobox.Locator("input").InputValueAsync()).Trim();
    }

    /// <summary>Returns true if the "Exclude public holidays from sickness" checkbox is currently checked.</summary>
    public async Task<bool> IsExcludePublicHolidaysFromSicknessCheckedAsync()
    {
        var wrapper = page.Locator(".e-checkbox-wrapper")
            .Filter(new() { HasText = "Exclude public holidays from sickness" });
        return await wrapper.Locator("input[type='checkbox']").IsCheckedAsync();
    }

    /// <summary>
    /// Sets the "Exclude public holidays from sickness" checkbox to the desired checked state.
    /// Only clicks if the current state differs from the requested one.
    /// </summary>
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

    /// <summary>Returns true if the "Exclude public holidays from leave" checkbox is currently checked.</summary>
    public async Task<bool> IsExcludePublicHolidaysFromLeaveCheckedAsync()
    {
        var wrapper = page.Locator(".e-checkbox-wrapper")
            .Filter(new() { HasText = "Exclude public holidays from leave" });
        return await wrapper.Locator("input[type='checkbox']").IsCheckedAsync();
    }

    /// <summary>
    /// Sets the "Exclude public holidays from leave" checkbox to the desired checked state.
    /// Only clicks if the current state differs from the requested one.
    /// </summary>
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

    /// <summary>Returns true if the "Display salary to employees on their profile" checkbox is currently checked.</summary>
    public async Task<bool> IsDisplaySalaryOnEmployeeProfileCheckedAsync()
    {
        var wrapper = page.Locator(".e-checkbox-wrapper")
            .Filter(new() { HasText = "Display salary to employees on their profile" });
        return await wrapper.Locator("input[type='checkbox']").IsCheckedAsync();
    }

    /// <summary>
    /// Sets the "Display salary to employees on their profile" checkbox to the desired checked state.
    /// Only clicks if the current state differs from the requested one.
    /// </summary>
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

    /// <summary>Sets the "Fit Note Required After (Days)" numeric field. Pass null to clear it.</summary>
    public async Task SetFitNoteRequiredAfterDaysAsync(int? days) =>
        await FillNullableNumericAndVerifyAsync(NumericBoxByLabel(".col-md-4", "Fit Note Required After (Days)"), days);

    /// <summary>Returns the current value of the "Fit Note Required After (Days)" numeric field, or null if empty.</summary>
    public async Task<int?> GetFitNoteRequiredAfterDaysAsync()
    {
        var input = NumericBoxByLabel(".col-md-4", "Fit Note Required After (Days)");
        var value = await input.InputValueAsync();
        return int.TryParse(value, out var parsed) ? parsed : null;
    }

    /// <summary>Sets the "Return-to-Work Review Required After (Days)" numeric field. Pass null to clear it.</summary>
    public async Task SetReturnToWorkRequiredAfterDaysAsync(int? days) =>
        await FillNullableNumericAndVerifyAsync(NumericBoxByLabel(".col-md-4", "Return-to-Work Review Required After (Days)"), days);

    /// <summary>Returns the current value of the "Return-to-Work Review Required After (Days)" numeric field, or null if empty.</summary>
    public async Task<int?> GetReturnToWorkRequiredAfterDaysAsync()
    {
        var input = NumericBoxByLabel(".col-md-4", "Return-to-Work Review Required After (Days)");
        var value = await input.InputValueAsync();
        return int.TryParse(value, out var parsed) ? parsed : null;
    }

    /// <summary>
    /// The "Default Acknowledgement Statement" HrTextBox (Multiline) in the Settings tab's
    /// "Document Acknowledgement" section. Located by its fixed placeholder text — the only
    /// rendering of that placeholder on this page — since HrTextBox uses FloatLabelType.Never by
    /// default (a real HTML placeholder attribute), unlike the numeric fields elsewhere on this
    /// tab that use FloatLabelType.Auto floating labels (see NumericBoxByLabel's doc comment).
    /// </summary>
    private ILocator DefaultAcknowledgementStatementTextArea =>
        page.GetByPlaceholder("I confirm that I have read and understood this document.");

    public Task SetDefaultAcknowledgementStatementAsync(string value) =>
        DefaultAcknowledgementStatementTextArea.FillAsync(value);

    public Task<string> GetDefaultAcknowledgementStatementAsync() =>
        DefaultAcknowledgementStatementTextArea.InputValueAsync();

    /// <summary>
    /// Selects a preset from the "Default Notice Period" dropdown — one of "1 week", "2 weeks",
    /// "1 month", "2 months", "3 months", "6 months", or "Custom duration" (which reveals the
    /// Unit/Length controls; see <see cref="WaitForNoticePeriodCustomControlsAsync"/>).
    /// </summary>
    public async Task SelectNoticePeriodPresetAsync(string presetLabel)
    {
        var group = page.Locator(".col-md-4")
            .Filter(new() { HasText = "Default Notice Period" })
            .First;
        await group.Locator("span[role='combobox']").First.ClickAsync();
        await page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });
        await page.Locator(".e-popup.e-ddl .e-list-item")
            .Filter(new() { HasText = presetLabel })
            .First
            .ClickAsync();
        await page.WaitForSelectorAsync(".e-popup.e-ddl", new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    /// <summary>Returns the currently displayed value of the "Default Notice Period" dropdown.</summary>
    public async Task<string> GetNoticePeriodPresetAsync()
    {
        var group = page.Locator(".col-md-4")
            .Filter(new() { HasText = "Default Notice Period" })
            .First;
        var combobox = group.Locator("span[role='combobox']").First;
        return (await combobox.Locator("input").InputValueAsync()).Trim();
    }

    /// <summary>
    /// Waits for the custom-duration Unit/Length controls to render after selecting the
    /// "Custom duration" notice period preset. They're conditionally rendered (@if in the
    /// Razor markup) based on server-side state, so there's a Blazor round-trip after the
    /// preset dropdown's popup closes before they appear.
    /// </summary>
    public Task WaitForNoticePeriodCustomControlsAsync() =>
        page.Locator(".col-md-4").Filter(new() { HasText = "Unit" }).First
            .WaitForAsync(new() { Timeout = 10_000 });

    /// <summary>
    /// Selects a value ("Weeks" or "Months") from the custom-duration "Unit" dropdown. Only
    /// present once "Custom duration" has been selected in the notice period preset dropdown —
    /// call <see cref="WaitForNoticePeriodCustomControlsAsync"/> first.
    /// </summary>
    public async Task SelectNoticePeriodUnitAsync(string unitLabel)
    {
        var group = page.Locator(".col-md-4")
            .Filter(new() { HasText = "Unit" })
            .First;
        await group.Locator("span[role='combobox']").First.ClickAsync();
        await page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });
        await page.Locator(".e-popup.e-ddl .e-list-item")
            .Filter(new() { HasText = unitLabel })
            .First
            .ClickAsync();
        await page.WaitForSelectorAsync(".e-popup.e-ddl", new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    /// <summary>Returns the currently displayed value of the custom-duration "Unit" dropdown.</summary>
    public async Task<string> GetNoticePeriodUnitAsync()
    {
        var group = page.Locator(".col-md-4")
            .Filter(new() { HasText = "Unit" })
            .First;
        var combobox = group.Locator("span[role='combobox']").First;
        return (await combobox.Locator("input").InputValueAsync()).Trim();
    }

    /// <summary>Sets the custom-duration "Length" numeric field. Only present once "Custom duration" is selected.</summary>
    public async Task SetNoticePeriodLengthAsync(int length) =>
        await FillNumericAndVerifyAsync(NumericBoxByLabel(".col-md-4", "Length"), length.ToString(), length);

    /// <summary>Returns the current value of the custom-duration "Length" numeric field.</summary>
    public async Task<int> GetNoticePeriodLengthAsync()
    {
        var input = NumericBoxByLabel(".col-md-4", "Length");
        var value = await input.InputValueAsync();
        return int.Parse(value);
    }

    /// <summary>Returns true if the "Automatically disable system access on the employee's leaving date" checkbox is currently checked.</summary>
    public async Task<bool> IsAutoDisableAccessOnLeavingDateCheckedAsync()
    {
        var wrapper = page.Locator(".e-checkbox-wrapper")
            .Filter(new() { HasText = "Automatically disable system access on the employee's leaving date" });
        return await wrapper.Locator("input[type='checkbox']").IsCheckedAsync();
    }

    /// <summary>
    /// Sets the "Automatically disable system access on the employee's leaving date" checkbox to
    /// the desired checked state. Only clicks if the current state differs from the requested one.
    /// </summary>
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

    // ── Employee Numbering ──────────────────────────────────────────────────────

    /// <summary>Selects a value ("Manual" or "Automatic") from the "Numbering Mode" dropdown.</summary>
    public async Task SelectEmployeeNumberModeAsync(string modeLabel)
    {
        var group = page.Locator(".col-md-4")
            .Filter(new() { HasText = "Numbering Mode" })
            .First;
        await group.Locator("span[role='combobox']").First.ClickAsync();
        await page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });
        await page.Locator(".e-popup.e-ddl .e-list-item")
            .Filter(new() { HasText = modeLabel })
            .First
            .ClickAsync();
        await page.WaitForSelectorAsync(".e-popup.e-ddl", new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    /// <summary>Returns the currently displayed value of the "Numbering Mode" dropdown.</summary>
    public async Task<string> GetEmployeeNumberModeAsync()
    {
        var group = page.Locator(".col-md-4")
            .Filter(new() { HasText = "Numbering Mode" })
            .First;
        var combobox = group.Locator("span[role='combobox']").First;
        return (await combobox.Locator("input").InputValueAsync()).Trim();
    }

    /// <summary>
    /// Returns true if the Automatic-only Employee Numbering fields (Prefix, Next Number,
    /// Minimum Numeric Length, live preview) are visible — they're only rendered (@if in the
    /// Razor markup) while Numbering Mode is "Automatic" (see CompanySettingsTab.razor).
    /// </summary>
    public Task<bool> IsEmployeeNumberAutomaticFieldsVisibleAsync() =>
        page.GetByPlaceholder("e.g. EMP-").IsVisibleAsync();

    /// <summary>Sets the "Prefix" HrTextBox. Only present while Numbering Mode is "Automatic".</summary>
    public Task SetEmployeeNumberPrefixAsync(string value) =>
        page.GetByPlaceholder("e.g. EMP-").FillAsync(value);

    public Task<string> GetEmployeeNumberPrefixAsync() =>
        page.GetByPlaceholder("e.g. EMP-").InputValueAsync();

    /// <summary>Sets the "Next Number" numeric field. Only present while Numbering Mode is "Automatic".</summary>
    public Task SetNextEmployeeNumberAsync(int value) =>
        FillNumericAndVerifyAsync(NumericBoxByLabel(".col-md-4", "Next Number"), value.ToString(), value);

    public async Task<int> GetNextEmployeeNumberAsync()
    {
        var input = NumericBoxByLabel(".col-md-4", "Next Number");
        var value = await input.InputValueAsync();
        return int.Parse(value);
    }

    /// <summary>Sets the "Minimum Numeric Length" numeric field. Only present while Numbering Mode is "Automatic".</summary>
    public Task SetEmployeeNumberMinimumLengthAsync(int value) =>
        FillNumericAndVerifyAsync(NumericBoxByLabel(".col-md-4", "Minimum Numeric Length"), value.ToString(), value);

    public async Task<int> GetEmployeeNumberMinimumLengthAsync()
    {
        var input = NumericBoxByLabel(".col-md-4", "Minimum Numeric Length");
        var value = await input.InputValueAsync();
        return int.Parse(value);
    }

    /// <summary>
    /// Returns the live "Preview: XXXX" text shown below the Employee Numbering fields while in
    /// Automatic mode (see CompanySettingsTab.razor's PreviewEmployeeNumber computed property).
    /// </summary>
    public async Task<string?> GetEmployeeNumberPreviewAsync()
    {
        var paragraph = page.Locator("p").Filter(new() { HasText = "Preview:" }).First;
        return await paragraph.IsVisibleAsync() ? (await paragraph.TextContentAsync())?.Trim() : null;
    }

    /// <summary>
    /// The "Backfill Employee Numbers…" button in the Employee Numbering section. Only rendered
    /// (@if in CompanySettingsTab.razor) while Numbering Mode is "Automatic" — see
    /// <see cref="IsEmployeeNumberAutomaticFieldsVisibleAsync"/> for the same gating on the other
    /// Automatic-only fields.
    /// </summary>
    private ILocator BackfillEmployeeNumbersButton =>
        page.GetByRole(AriaRole.Button, new() { Name = "Backfill Employee Numbers…" });

    public Task<bool> IsBackfillEmployeeNumbersButtonVisibleAsync() =>
        BackfillEmployeeNumbersButton.IsVisibleAsync();

    public Task OpenBackfillEmployeeNumbersDialogAsync() =>
        BackfillEmployeeNumbersButton.ClickAsync();

    /// <summary>
    /// The "Backfill Employee Timeline…" button in the "Employee Timeline" subsection. Only rendered
    /// (@if in CompanySettingsTab.razor) while Session.CanManageEmployees is true (mirrors the
    /// server-side "employee:manage" policy) — note this is a *different* gate than the
    /// Session.CanManageCompany gate on the Settings tab itself.
    /// </summary>
    private ILocator BackfillEmployeeTimelineButton =>
        page.GetByRole(AriaRole.Button, new() { Name = "Backfill Employee Timeline…" });

    public Task<bool> IsBackfillEmployeeTimelineButtonVisibleAsync() =>
        BackfillEmployeeTimelineButton.IsVisibleAsync();

    public Task OpenBackfillEmployeeTimelineDialogAsync() =>
        BackfillEmployeeTimelineButton.ClickAsync();

    // ── Save ───────────────────────────────────────────────────────────────────

    public async Task SaveAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    public async Task<bool> HasErrorAsync() =>
        await page.Locator(".alert-danger, .validation-message").First.IsVisibleAsync();

    /// <summary>Fills the company Name field on the Profile tab.</summary>
    public Task FillCompanyNameInputAsync(string value) =>
        page.GetByPlaceholder("Company name").FillAsync(value);

    public async Task<string> GetCompanyNameInputValueAsync() =>
        await page.GetByPlaceholder("Company name").InputValueAsync();

    // ── Close / unsaved-changes prompt (EditPageBase) ──────────────────────────

    private ILocator UnsavedChangesDialog => page.Locator("[role='dialog']:has-text('Unsaved Changes')");

    public Task ClickCloseAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true }).ClickAsync();

    public Task<bool> IsUnsavedChangesDialogVisibleAsync() =>
        UnsavedChangesDialog.IsVisibleAsync();

    /// <summary>
    /// Close navigates to "/" (CompanyEdit has no dedicated list page) — but Home.razor's
    /// role-based landing redirect immediately bounces a CompanyAdministrator-only user (who has
    /// no HR/Recruitment/Manager dashboard) straight back to this same Company edit page, so
    /// that's the URL that actually settles. See AppSession.LandingUrl.
    /// </summary>
    public async Task CloseAndWaitForDashboardAsync(string baseUrl, Guid companyId)
    {
        await ClickCloseAsync();
        await page.WaitForURLAsync($"{baseUrl}/companies/{companyId}/edit", new() { Timeout = 15_000 });
    }

    public async Task ConfirmDiscardChangesAsync(string baseUrl, Guid companyId)
    {
        await UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Discard Changes" }).ClickAsync();
        await page.WaitForURLAsync($"{baseUrl}/companies/{companyId}/edit", new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Choosing "Save" from the unsaved-changes prompt always navigates away on success — unlike
    /// the page's own Save button, which stays put and shows an inline success banner instead.
    /// </summary>
    public async Task ConfirmSaveFromUnsavedChangesDialogAsync(string baseUrl, Guid companyId)
    {
        await UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await page.WaitForURLAsync($"{baseUrl}/companies/{companyId}/edit", new() { Timeout = 15_000 });
    }

    public Task CancelUnsavedChangesDialogAsync() =>
        UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
}
