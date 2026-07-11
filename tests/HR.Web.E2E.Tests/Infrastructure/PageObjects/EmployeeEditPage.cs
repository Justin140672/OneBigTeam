using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the employee edit/create page.
/// Covers the new-employee form and the Employment tab of existing employees.
/// Routes: /companies/{id}/employees/new  and  /companies/{id}/employees/{id}
/// </summary>
public sealed class EmployeeEditPage(IPage page, string baseUrl)
{
    public async Task GoToNewAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/employees/new");
        // Wait for Syncfusion to initialise — span[role='combobox'] only appears after
        // Blazor's interactive render, ensuring the form's event handlers are wired up.
        await page.WaitForSelectorAsync("span[role='combobox']", new() { Timeout = 20_000 });
    }

    public async Task GoToAsync(Guid companyId, Guid employeeId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/employees/{employeeId}");
        // span[role='combobox'] (SfDropDownList) only appears after Blazor's interactive
        // render, confirming the circuit is connected and event handlers are wired up.
        await page.WaitForSelectorAsync("span[role='combobox']", new() { Timeout = 20_000 });
    }

    /// <summary>
    /// Navigates directly to the employee edit page with a query string appended (e.g.
    /// "tab=onboarding") — used to verify deep-link tab activation (see EmployeeEdit.razor's
    /// LoadAsync, which maps the "tab" query parameter to an initial SfTab selected index).
    /// </summary>
    public async Task GoToAsync(Guid companyId, Guid employeeId, string query)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/employees/{employeeId}?{query}");
        await page.WaitForSelectorAsync("span[role='combobox']", new() { Timeout = 20_000 });
    }

    // ── New Employee — Personal Information ───────────────────────────────────

    public async Task FillFirstNameAsync(string value) =>
        await page.GetByPlaceholder("First name", new() { Exact = true }).FillAsync(value);

    public async Task FillLastNameAsync(string value) =>
        await page.GetByPlaceholder("Last name").FillAsync(value);

    public async Task FillWorkEmailAsync(string value) =>
        await page.GetByPlaceholder("work@company.com").FillAsync(value);

    public async Task FillStartDateAsync(string ddMMyyyy)
    {
        var inputs = page.Locator(".e-date-wrapper input.e-input");
        // The start date picker is the first date input on the new employee form.
        await inputs.First.ClickAsync();
        await inputs.First.FillAsync(ddMMyyyy);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillDateOfBirthAsync(string ddMMyyyy)
    {
        // Date of birth is the second date picker on the new employee form.
        var inputs = page.Locator(".e-date-wrapper input.e-input");
        await inputs.Nth(1).ClickAsync();
        await inputs.Nth(1).FillAsync(ddMMyyyy);
        await page.Keyboard.PressAsync("Tab");
    }

    /// <summary>Selects a value from a Syncfusion SfDropDownList identified by nearby label text.</summary>
    public async Task SelectDropdownAsync(string labelText, string optionText)
    {
        var group = page.Locator(".col-md-6, .col-md-4")
            .Filter(new() { HasText = labelText })
            .First;
        await group.Locator("span[role='combobox']").First.ClickAsync();
        await page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });
        await page.Locator(".e-popup.e-ddl .e-list-item")
            .Filter(new() { HasText = optionText })
            .First
            .ClickAsync();
    }

    // ── Employment Tab ─────────────────────────────────────────────────────────

    public async Task OpenEmploymentTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Employment" }).ClickAsync();
        // Wait for the Employment-tab-specific heading — the generic .card-header selector
        // would resolve immediately against the Details tab's already-rendered card headers.
        await page.WaitForSelectorAsync(".card-header:has-text('Employment Details')", new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Selects a manager from the Manager dropdown on the Employment tab.
    /// </summary>
    public async Task SelectManagerAsync(string managerNameFragment)
    {
        var managerGroup = page.Locator(".col-md-4, .col-12")
            .Filter(new() { HasText = "Manager" })
            .First;
        await managerGroup.Locator("span[role='combobox']").First.ClickAsync();
        await page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });
        // Type into the filter input — required for AllowFiltering dropdowns.
        var filterInput = page.Locator(".e-popup.e-ddl:visible input.e-input").First;
        await filterInput.FillAsync(managerNameFragment);
        await page.WaitForSelectorAsync(".e-popup.e-ddl .e-list-item:not(.e-hide)", new() { Timeout = 15_000 });
        await page.Locator(".e-popup.e-ddl .e-list-item:not(.e-hide)")
            .Filter(new() { HasText = managerNameFragment })
            .First
            .ClickAsync();
    }

    /// <summary>
    /// Clicks the single page-level Save button (persistent across all tabs, below the SfTab).
    /// Saves both the Details and Employment tabs together and navigates to the employee list on success.
    /// </summary>
    public async Task ClickSaveChangesAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    // ── Save (new employee form) ───────────────────────────────────────────────

    public async Task SaveNewEmployeeAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        // Navigates to the employee list on success.
        await page.WaitForURLAsync("**/employees", new() { Timeout = 20_000 });
        // With prerender:false the circuit connects after navigation, wait for the grid.
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public async Task<bool> HasProbationSummaryAsync() =>
        await page.Locator("[data-testid='probation-summary']").IsVisibleAsync();

    /// <summary>
    /// Returns true if the "From Position Profile" read-only defaults summary card is visible
    /// on the new-employee form (shown after a Position Profile is selected).
    /// </summary>
    public async Task<bool> HasPositionProfileDefaultsSummaryAsync() =>
        await page.Locator("[data-testid='position-profile-defaults-summary']").IsVisibleAsync();

    /// <summary>Reads the current value of the Department dropdown's visible text on the new-employee form.</summary>
    public async Task<string?> GetSelectedDepartmentTextAsync()
    {
        var group = page.Locator(".col-md-4").Filter(new() { HasText = "Department" }).First;
        return await group.Locator(".e-input-group input").First.InputValueAsync();
    }

    /// <summary>Reads the current value of the Location dropdown's visible text (new-employee form or Employment tab).</summary>
    public async Task<string?> GetSelectedLocationTextAsync()
    {
        var group = page.Locator(".col-md-4").Filter(new() { HasText = "Location" }).First;
        return await group.Locator(".e-input-group input").First.InputValueAsync();
    }

    public async Task<bool> HasErrorAsync() =>
        await page.Locator(".alert-danger, .validation-message").First.IsVisibleAsync();

    /// <summary>
    /// Returns true if a field-level validation message containing <paramref name="messageText"/>
    /// is visible (e.g. "Employee number is required." from EmployeeProfileEditModel's
    /// [Required(ErrorMessage = ...)] attributes) — used to verify a specific required field's
    /// validation, rather than the generic "some error is present" check in <see cref="HasErrorAsync"/>.
    /// </summary>
    public async Task<bool> HasValidationMessageAsync(string messageText) =>
        await page.Locator(".validation-message").Filter(new() { HasText = messageText }).First.IsVisibleAsync();

    /// <summary>Fills the Employee Number field on the Employment tab.</summary>
    public async Task FillEmployeeNumberAsync(string value) =>
        await page.GetByPlaceholder("e.g. EMP-001").FillAsync(value);

    // ── Close / unsaved-changes prompt (EditPageBase) ──────────────────────────

    private ILocator UnsavedChangesDialog => page.Locator("[role='dialog']:has-text('Unsaved Changes')");

    public Task ClickCloseAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true }).ClickAsync();

    public Task<bool> IsUnsavedChangesDialogVisibleAsync() =>
        UnsavedChangesDialog.IsVisibleAsync();

    public async Task ConfirmDiscardChangesAsync()
    {
        await UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Discard Changes" }).ClickAsync();
        await page.WaitForURLAsync("**/employees", new() { Timeout = 15_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public async Task ConfirmSaveFromUnsavedChangesDialogAsync()
    {
        await UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await page.WaitForURLAsync("**/employees", new() { Timeout = 15_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public Task CancelUnsavedChangesDialogAsync() =>
        UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();

    // ── Compensation Tab ────────────────────────────────────────────────────────

    public async Task OpenCompensationTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Compensation" }).ClickAsync();
        await page.WaitForSelectorAsync(
            "[data-testid='current-compensation-panel'], [data-testid='no-compensation-message'], [data-testid='no-current-compensation-message']",
            new() { Timeout = 15_000 });
    }

    public Task<bool> HasCurrentCompensationPanelAsync() =>
        page.Locator("[data-testid='current-compensation-panel']").IsVisibleAsync();

    public async Task<string?> GetCompensationFieldTextAsync(string testId)
    {
        var locator = page.Locator($"[data-testid='{testId}']");
        return await locator.IsVisibleAsync() ? (await locator.TextContentAsync())?.Trim() : null;
    }

    public async Task ClickAddCompensationAsync()
    {
        await page.Locator("[data-testid='add-compensation-btn']").ClickAsync();
        await page.Locator("[role='dialog'].add-compensation-dialog").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    public async Task FillAddCompensationEffectiveFromAsync(string ddMMyyyy)
    {
        var input = page.Locator(".add-compensation-dialog .e-date-wrapper input.e-input").First;
        await input.ClickAsync();
        await input.FillAsync(ddMMyyyy);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task SelectAddCompensationSalaryTypeAsync(string salaryType)
    {
        await page.Locator(".add-compensation-dialog span[role='combobox']").First.ClickAsync();
        await page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });
        await page.Locator(".e-popup.e-ddl .e-list-item")
            .Filter(new() { HasText = salaryType })
            .First
            .ClickAsync();
    }

    // Salary is an SfNumericTextBox with FloatLabelType.Auto, which renders its Placeholder
    // prop as a floating label rather than a native HTML placeholder attribute, so
    // GetByPlaceholder never matches it (see CompanyEditPage.NumericBoxByLabel for the same
    // caveat). Salary is the first e-numerictextbox in the dialog (before Hours Per Week/FTE).
    public Task FillAddCompensationSalaryAsync(string value) =>
        FillNumericAndVerifyAsync(page.Locator(".add-compensation-dialog input.e-numerictextbox").First, value, decimal.Parse(value));

    public Task FillAddCompensationCurrencyAsync(string value) =>
        page.Locator(".add-compensation-dialog").GetByPlaceholder("e.g. GBP").FillAsync(value);

    public async Task SubmitAddCompensationDialogAsync()
    {
        await page.Locator(".add-compensation-dialog .e-footer-content button:has-text('Add')").ClickAsync();
        await page.Locator("[role='dialog'].add-compensation-dialog").WaitForAsync(
            new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    public Task<bool> HasAddCompensationDialogErrorAsync() =>
        page.Locator(".add-compensation-dialog .alert-danger").IsVisibleAsync();

    public ILocator CompensationHistoryRow(string effectiveFromFragment) =>
        page.Locator("[data-testid='compensation-history-grid'] .e-row").Filter(new() { HasText = effectiveFromFragment });

    public Task ClickEditCompensationRowAsync(string effectiveFromFragment) =>
        CompensationHistoryRow(effectiveFromFragment).First.GetByTitle("Edit").ClickAsync();

    public Task ClickDeleteCompensationRowAsync(string effectiveFromFragment) =>
        CompensationHistoryRow(effectiveFromFragment).First.GetByTitle("Delete").ClickAsync();

    public async Task ConfirmDeleteCompensationAsync()
    {
        // Clicking "Yes" triggers an async delete + grid reload round-trip; ClickAsync only
        // waits for the click event to dispatch, not for that round-trip, so callers that
        // immediately check row visibility can race ahead of the reload. Wait for the button
        // itself to disappear (it only renders for the row mid-confirmation) as a signal the
        // grid has actually re-rendered with fresh data.
        var yesButton = page.Locator("[data-testid='compensation-history-grid']").GetByRole(AriaRole.Button, new() { Name = "Yes" });
        await yesButton.ClickAsync();
        await yesButton.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    public async Task FillEditCompensationSalaryAsync(string value)
    {
        await page.Locator("[role='dialog'].edit-future-compensation-dialog").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        // See FillAddCompensationSalaryAsync — Salary is a FloatLabelType.Auto SfNumericTextBox,
        // so it must be targeted by its e-numerictextbox class rather than GetByPlaceholder.
        await FillNumericAndVerifyAsync(page.Locator(".edit-future-compensation-dialog input.e-numerictextbox").First, value, decimal.Parse(value));
    }

    public async Task SubmitEditCompensationDialogAsync()
    {
        await page.Locator(".edit-future-compensation-dialog .e-footer-content button:has-text('Save')").ClickAsync();
        await page.Locator("[role='dialog'].edit-future-compensation-dialog").WaitForAsync(
            new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    // FillAsync sets a Syncfusion SfNumericTextBox's DOM value through CDP directly, which
    // bypasses the component's own JS keyup/input listeners that sync the typed value back to
    // the Blazor-bound model — so a value that visually "fills" never actually round-trips to
    // the server (see CompanyEditPage.TypeIntoNumericInputAsync for the same issue). Click-to-
    // focus, select-all, delete, then type each character for real.
    private async Task TypeIntoNumericInputAsync(ILocator input, string value)
    {
        await input.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.PressAsync("Delete");
        if (value.Length > 0)
            await input.PressSequentiallyAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    /// <summary>
    /// Fills a numeric input and confirms the parsed value actually stuck before returning,
    /// retrying if not — a bare "fire and forget" fill can race with Blazor's server round-trip
    /// for the two-way bound value (see CompanyEditPage.FillNumericAndVerifyAsync for the same
    /// issue, originally observed with DefaultHolidayAllowance reverting after save+reload).
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

    // ── Audit Tab ───────────────────────────────────────────────────────────────

    public async Task OpenAuditTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Audit" }).ClickAsync();
        await page.WaitForSelectorAsync(
            "[data-testid='audit-history-grid'], .alert-secondary",
            new() { Timeout = 15_000 });
    }

    public ILocator AuditHistoryRow(string actionFragment) =>
        page.Locator("[data-testid='audit-history-grid'] .e-row").Filter(new() { HasText = actionFragment });

    public Task ClickViewAuditRowAsync(string actionFragment) =>
        AuditHistoryRow(actionFragment).First.GetByTitle("View").ClickAsync();

    public async Task<bool> HasAuditDetailDialogAsync() =>
        await page.Locator("[role='dialog'].audit-history-detail-dialog").IsVisibleAsync();

    public async Task<string?> GetAuditDetailDialogTextAsync() =>
        await page.Locator("[role='dialog'].audit-history-detail-dialog").TextContentAsync();

    public async Task CloseAuditDetailDialogAsync()
    {
        await page.Locator(".audit-history-detail-dialog .e-footer-content button:has-text('Close')").ClickAsync();
        await page.Locator("[role='dialog'].audit-history-detail-dialog").WaitForAsync(
            new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    // ── Probation Tab ──────────────────────────────────────────────────────────

    public async Task OpenProbationTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Probation" }).ClickAsync();
        // Wait for the tab content to render — either a card or "No probation record" alert.
        await page.WaitForSelectorAsync(".card, .alert-secondary", new() { Timeout = 15_000 });
    }

    /// <summary>Returns true if the probation period summary panel (progress bar card) is visible.</summary>
    public async Task<bool> HasProbationPeriodSummaryPanelAsync() =>
        await page.Locator(".progress").IsVisibleAsync();

    /// <summary>Returns true if the Syncfusion review history grid is visible on the Probation tab.</summary>
    public async Task<bool> HasProbationReviewsGridAsync() =>
        await page.Locator(".e-grid").IsVisibleAsync();

    /// <summary>Returns the text of the probation status badge on the Probation tab summary panel.</summary>
    public async Task<string?> GetProbationStatusBadgeTextAsync()
    {
        var badge = page.Locator(".card .badge").First;
        return await badge.IsVisibleAsync() ? (await badge.TextContentAsync())?.Trim() : null;
    }

    /// <summary>
    /// Returns the status badge text for the first review row in the review history grid
    /// whose ReviewType cell contains <paramref name="reviewTypeFragment"/>.
    /// </summary>
    public async Task<string?> GetReviewStatusInGridAsync(string reviewTypeFragment)
    {
        await page.WaitForSelectorAsync(".e-grid .e-row", new() { Timeout = 10_000 });

        var rows = await page.Locator(".e-grid .e-row").AllAsync();
        foreach (var row in rows)
        {
            var text = await row.TextContentAsync();
            if (text?.Contains(reviewTypeFragment, StringComparison.OrdinalIgnoreCase) != true)
                continue;

            // Status badge is within the row — grab the first badge element.
            var badge = row.Locator(".badge").First;
            if (await badge.IsVisibleAsync())
                return (await badge.TextContentAsync())?.Trim();
        }

        return null;
    }

    // ── Tasks Tab ───────────────────────────────────────────────────────────────

    public async Task OpenTasksTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Tasks" }).ClickAsync();
        await page.WaitForSelectorAsync(".e-grid, .task-cell, p", new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Clicks the task row (by task id) in the admin Tasks tab grid, opening TaskViewDialog.
    /// Use TaskViewPage.WaitForLoadedAsync (or its own methods) to read the opened task's content.
    /// </summary>
    public async Task ClickTaskAsync(Guid taskId)
    {
        var row = page.Locator($"[data-testid='task-view-btn-{taskId}']");
        await row.WaitForAsync(new() { Timeout = 15_000 });
        await row.ClickAsync();
        await page.WaitForSelectorAsync(".task-view-dialog", new() { Timeout = 15_000 });
    }

    // ── Sickness Tab ────────────────────────────────────────────────────────────

    public async Task OpenSicknessTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Sickness" }).ClickAsync();
        await page.WaitForSelectorAsync(".card, .alert-secondary", new() { Timeout = 15_000 });
    }

    public async Task<bool> HasSicknessGridAsync() =>
        await page.Locator(".e-grid").IsVisibleAsync();

    public async Task OpenRecordSicknessDialogAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Record Sickness" }).ClickAsync();
        await page.WaitForSelectorAsync("[role='dialog'].record-sickness-dialog", new() { Timeout = 10_000 });
    }

    /// <summary>Selects a category in the (currently open) Record Sickness dialog.</summary>
    public async Task SelectRecordSicknessCategoryAsync(string categoryName)
    {
        var group = page.Locator("[role='dialog'].record-sickness-dialog .col-12")
            .Filter(new() { HasText = "Category" })
            .First;
        await group.Locator("span[role='combobox']").First.ClickAsync();
        await page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });
        await page.Locator(".e-popup.e-ddl .e-list-item")
            .Filter(new() { HasText = categoryName })
            .First
            .ClickAsync();
    }

    /// <summary>Fills the Start Date field in the (currently open) Record Sickness dialog.</summary>
    public async Task FillRecordSicknessStartDateAsync(string ddMMyyyy)
    {
        var input = page.Locator("[role='dialog'].record-sickness-dialog .e-date-wrapper input.e-input").First;
        await input.ClickAsync();
        await input.FillAsync(ddMMyyyy);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task SubmitRecordSicknessAsync()
    {
        await page.Locator("[role='dialog'].record-sickness-dialog")
            .GetByRole(AriaRole.Button, new() { Name = "Record", Exact = true })
            .ClickAsync();
        await page.Locator("[role='dialog'].record-sickness-dialog")
            .WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }

    public async Task<bool> HasRecordSicknessErrorAsync() =>
        await page.Locator("[role='dialog'].record-sickness-dialog .alert-danger").IsVisibleAsync();

    /// <summary>
    /// Returns the status badge text for the sickness grid row whose Start Date column
    /// contains <paramref name="startDateddMMMyyyy"/> (e.g. "05 Jul 2026", matching the
    /// grid's "dd MMM yyyy" display format).
    /// </summary>
    public async Task<string?> GetSicknessStatusBadgeForStartDateAsync(string startDateddMMMyyyy)
    {
        await page.WaitForSelectorAsync(".e-grid .e-row", new() { Timeout = 10_000 });

        var row = page.Locator(".e-grid .e-row")
            .Filter(new() { HasText = startDateddMMMyyyy })
            .First;

        var badge = row.Locator(".badge").First;
        return await badge.IsVisibleAsync() ? (await badge.TextContentAsync())?.Trim() : null;
    }

    /// <summary>Clicks the "Close" action button on the grid row matching the given start date.</summary>
    public async Task StartCloseSicknessRecordAsync(string startDateddMMMyyyy)
    {
        var row = page.Locator(".e-grid .e-row")
            .Filter(new() { HasText = startDateddMMMyyyy })
            .First;
        // Button text is "Close" (the "Close record" title attribute is only a tooltip —
        // accessible name computation prefers the visible text content).
        await row.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true }).ClickAsync();
        await page.WaitForSelectorAsync("[role='dialog'].close-sickness-record-dialog", new() { Timeout = 10_000 });
    }

    /// <summary>Fills the End Date field in the (currently open) Close Sickness Record dialog.</summary>
    public async Task FillCloseSicknessEndDateAsync(string ddMMyyyy)
    {
        var input = page.Locator("[role='dialog'].close-sickness-record-dialog .e-date-wrapper input.e-input").First;
        await input.ClickAsync();
        await input.FillAsync(ddMMyyyy);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task SubmitCloseSicknessRecordAsync()
    {
        await page.Locator("[role='dialog'].close-sickness-record-dialog")
            .GetByRole(AriaRole.Button, new() { Name = "Close Record" })
            .ClickAsync();
        await page.Locator("[role='dialog'].close-sickness-record-dialog")
            .WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }

    // ── Onboarding Tab ──────────────────────────────────────────────────────────

    public async Task OpenOnboardingTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Onboarding" }).ClickAsync();
        // Wait for the tab content to render — either the progress panel's progress bar
        // (a plan exists) or the "No onboarding plan found for this employee" empty state.
        await page.WaitForSelectorAsync(".progress, .hr-empty-state", new() { Timeout = 15_000 });
    }

    /// <summary>Returns true if the onboarding progress panel (status badge + progress bar) is visible.</summary>
    public async Task<bool> HasOnboardingProgressPanelAsync() =>
        await page.Locator(".progress").IsVisibleAsync();

    /// <summary>Returns true if the Onboarding Checklist card is visible.</summary>
    public async Task<bool> HasOnboardingChecklistAsync() =>
        await page.Locator(".card-header:has-text('Onboarding Checklist')").IsVisibleAsync();

    /// <summary>Returns true if the Onboarding Timeline card is visible.</summary>
    public async Task<bool> HasOnboardingTimelineAsync() =>
        await page.Locator(".card-header:has-text('Onboarding Timeline')").IsVisibleAsync();

    /// <summary>Returns the text of the onboarding plan status badge on the progress panel.</summary>
    public async Task<string?> GetOnboardingStatusBadgeTextAsync()
    {
        var badge = page.Locator(".card .badge").First;
        return await badge.IsVisibleAsync() ? (await badge.TextContentAsync())?.Trim() : null;
    }

    /// <summary>
    /// Returns the current onboarding progress percentage, read from the progress bar's
    /// aria-valuenow attribute (more robust than scraping the "NN%" caption text).
    /// </summary>
    public async Task<int> GetOnboardingProgressPercentAsync()
    {
        var bar = page.Locator(".progress .progress-bar");
        var value = await bar.GetAttributeAsync("aria-valuenow");
        return int.TryParse(value, out var percent) ? percent : 0;
    }

    /// <summary>
    /// Returns the status badge text ("Pending"/"In Progress"/"Completed"/"Overdue"/"Skipped")
    /// for the Onboarding Checklist row whose Task cell contains <paramref name="taskTitleFragment"/>.
    /// Scoped to the "Onboarding Checklist" card specifically, since the Outstanding Document
    /// Requests / Outstanding Asset Acknowledgements cards below it share the same table classes.
    /// </summary>
    public async Task<string?> GetOnboardingChecklistTaskStatusAsync(string taskTitleFragment)
    {
        var checklistCard = page.Locator(".card").Filter(new() { HasText = "Onboarding Checklist" }).First;
        var row = checklistCard.Locator("table tbody tr").Filter(new() { HasText = taskTitleFragment }).First;
        var badge = row.Locator(".badge");
        return await badge.IsVisibleAsync() ? (await badge.TextContentAsync())?.Trim() : null;
    }
}
