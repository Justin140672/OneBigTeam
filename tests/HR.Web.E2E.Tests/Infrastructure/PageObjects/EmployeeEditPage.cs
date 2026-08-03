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
        // Deep-linking can land on any tab, and not every tab renders a combobox (Onboarding and
        // Offboarding don't), so — unlike the other GoToAsync overloads above, which always land
        // on "Details" (which does) — wait on the tab list itself instead. It's rendered by the
        // same SfTab regardless of which tab query-string selects, so it's still a reliable
        // signal that Blazor's interactive circuit has connected.
        await page.WaitForSelectorAsync("[role='tablist']", new() { Timeout = 20_000 });
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
    public Task SelectDropdownAsync(string labelText, string optionText) =>
        DropDownSelector.SelectAsync(page, page.Locator(".col-md-6, .col-md-4").Filter(new() { HasText = labelText }).First, optionText);

    // ── Employee Overview header ────────────────────────────────────────────────

    /// <summary>
    /// Returns the text of the employee status badge shown next to the employee's name at the
    /// top of the page (e.g. "Active", "Leaving", "Former Employee" — see EmployeeEdit.razor's
    /// StatusDisplayName). Scoped to the "rounded-pill" class combo distinguishing it from the
    /// Reporting Chain card's own "Current Employee" badge and every lifecycle tab's own status
    /// badge further down the page — see the Playwright locator conventions around bare-class
    /// locators for why a plain ".badge" alone would be ambiguous here.
    /// </summary>
    public async Task<string?> GetEmployeeStatusBadgeTextAsync()
    {
        var badge = page.Locator(".badge.rounded-pill").First;
        return await badge.IsVisibleAsync() ? (await badge.TextContentAsync())?.Trim() : null;
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
    /// Reads the plain-text value of the Employment tab's read-only "Hours"/"FTE"/"Effective
    /// From" fields, which render alongside "Current Salary" (see EmployeeEmploymentTab.razor's
    /// CurrentHoursDisplay/CurrentFteDisplay/CurrentEffectiveFromDisplay).
    /// </summary>
    public async Task<string?> GetEmploymentTabReadOnlyFieldAsync(string labelText)
    {
        var group = page.Locator(".col-md-6, .col-md-4").Filter(new() { HasText = labelText }).First;
        var value = group.Locator("p.form-control-plaintext").First;
        return await value.IsVisibleAsync() ? (await value.TextContentAsync())?.Trim() : null;
    }

    /// <summary>
    /// Returns the trimmed card-header headings in DOM order on the (currently open) Employment
    /// tab — used to assert the "Organisation" card renders above the "Dates" card.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetEmploymentTabCardHeadingsAsync()
    {
        var headers = await page.Locator(".card-header h5").AllAsync();
        var result = new List<string>();
        foreach (var header in headers)
            result.Add((await header.TextContentAsync())?.Trim() ?? "");
        return result;
    }

    /// <summary>
    /// Selects a manager from the Manager dropdown on the Employment tab. DropDownSelector itself
    /// confirms Blazor's ValueChanged round-trip actually committed the selection before
    /// returning — see its own doc comment.
    /// </summary>
    public async Task SelectManagerAsync(string managerNameFragment)
    {
        var managerGroup = page.Locator(".col-md-4, .col-12")
            .Filter(new() { HasText = "Manager" })
            .First;
        await DropDownSelector.SelectAsync(page, managerGroup, managerNameFragment);
    }

    /// <summary>
    /// Clears the manager selection on the Employment tab by opening the Manager dropdown and
    /// selecting its prepended "No Manager" sentinel item (Id = Guid.Empty) — replaces the old
    /// ShowClearButton ("x" icon) approach, which was removed in favor of this explicit
    /// no-selection list item (see EmployeeEmploymentTab.razor's ManagerOption list, which
    /// prepends a Guid.Empty/"No Manager" entry rather than setting ShowClearButton="true").
    /// </summary>
    public async Task ClearManagerAsync()
    {
        var managerGroup = page.Locator(".col-md-4, .col-12")
            .Filter(new() { HasText = "Manager" })
            .First;
        await DropDownSelector.SelectAsync(page, managerGroup, "No Manager");
    }

    /// <summary>
    /// Clicks the single page-level Save button (persistent across all tabs, below the SfTab).
    /// Saves both the Details and Employment tabs together and navigates to the employee list on success.
    /// </summary>
    /// <remarks>
    /// Previously this only waited for the spinner to clear, which is true whether the save
    /// succeeded OR failed validation (e.g. EmployeeEmploymentTab.SaveCoreAsync's
    /// EditContext.Validate() failing, or UpdateEmploymentDetailsHandler returning a Conflict) —
    /// a failed save leaves the caller on the same edit page with stale/unsaved field values and
    /// no exception, so any later assertion about what got saved fails far from the actual cause.
    /// Explicitly checking for the error banner here turns that into an immediate, specific failure.
    /// </remarks>
    public async Task ClickSaveChangesAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });

        var errorBanner = page.Locator(".alert-danger").First;
        if (await errorBanner.IsVisibleAsync())
        {
            var message = (await errorBanner.TextContentAsync())?.Trim();
            throw new Exception($"Save failed: {message}");
        }
    }

    // ── Save (new employee form) ───────────────────────────────────────────────

    public async Task SaveNewEmployeeAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        // Navigates to the employee list on success.
        await page.WaitForURLAsync("**/employees", new() { Timeout = 20_000 });
        // With prerender:false the circuit connects after navigation, wait for the grid. ".e-grid"
        // alone isn't enough — Syncfusion populates ".e-row"/".e-rowcell" on a separate JS tick
        // after the grid element mounts, so callers that immediately click the new row (e.g.
        // ManagerDashboardTests.CreateEmployeeReportingToDavidAsync) can race an empty grid.
        await page.WaitForSelectorAsync(".e-grid .e-row, .e-grid .e-emptyrow", new() { Timeout = 20_000 });
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

    /// <summary>Reads the current value of the Manager dropdown's visible text on the Employment tab.</summary>
    public async Task<string?> GetSelectedManagerTextAsync()
    {
        var group = page.Locator(".col-md-4, .col-12").Filter(new() { HasText = "Manager" }).First;
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

    /// <summary>
    /// Returns true if the Employee Number text input is visible on the new-employee form —
    /// false when the company's numbering mode is Automatic, in which case the informational
    /// message below is shown instead (see <see cref="HasEmployeeNumberAutoAssignedMessageAsync"/>).
    /// </summary>
    public Task<bool> IsEmployeeNumberInputVisibleAsync() =>
        page.GetByPlaceholder("e.g. EMP-001").IsVisibleAsync();

    /// <summary>
    /// Returns true if the "An employee number will be assigned automatically when this employee
    /// is created." informational message is visible on the new-employee form (Automatic mode).
    /// </summary>
    public Task<bool> HasEmployeeNumberAutoAssignedMessageAsync() =>
        page.Locator("p").Filter(new() { HasText = "An employee number will be assigned automatically" }).First.IsVisibleAsync();

    /// <summary>
    /// Returns the "#EMP-001"-style employee number badge shown next to the status badge in the
    /// header of an existing employee's edit page, or null if not present.
    /// </summary>
    public async Task<string?> GetEmployeeNumberHeaderTextAsync()
    {
        var spans = await page.Locator("span.text-muted").AllAsync();
        foreach (var span in spans)
        {
            var text = (await span.TextContentAsync())?.Trim();
            if (text is not null && text.StartsWith('#'))
                return text;
        }
        return null;
    }

    // ── Notice period override (Employment tab, "Dates" card) ──────────────────
    //
    // Mirrors the "Override company default notice period" toggle on the Position Profile
    // edit page (see PositionProfileEditPage's own "Notice period override" section) — the
    // same three Syncfusion component types (SfCheckBox/SfDropDownList/SfNumericTextBox),
    // used the same way, just a different checkbox label ("Override notice period" rather
    // than "...company default...") and an additional read-only "Notice source" summary
    // alongside it (see EmployeeEmploymentTab.razor's Dates card).

    /// <summary>
    /// The "row g-3 mt-2" div containing the Unit dropdown and Length numeric field, which
    /// is only present in the DOM while "Override notice period" is checked. Same xpath
    /// sibling-traversal approach as PositionProfileEditPage.NoticePeriodOverrideRow, since
    /// the Unit dropdown here has no adjacent &lt;label&gt; to scope by either.
    /// </summary>
    private ILocator NoticePeriodOverrideRow =>
        page.Locator(".e-checkbox-wrapper")
            .Filter(new() { HasText = "Override notice period" })
            .Locator("xpath=following-sibling::div[contains(@class,'row')]");

    /// <summary>Checks/unchecks "Override notice period" and waits for the reveal/hide of its fields.</summary>
    public async Task SetOverrideNoticePeriodAsync(bool overrideEnabled)
    {
        var checkbox = page.GetByLabel("Override notice period");
        var isChecked = await checkbox.IsCheckedAsync();
        if (overrideEnabled && !isChecked)
        {
            await checkbox.CheckAsync();
            await NoticePeriodOverrideRow.WaitForAsync(new() { Timeout = 10_000 });
        }
        if (!overrideEnabled && isChecked)
        {
            await checkbox.UncheckAsync();
            await NoticePeriodOverrideRow.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
        }
    }

    public Task<bool> IsOverrideNoticePeriodCheckedAsync() =>
        page.GetByLabel("Override notice period").IsCheckedAsync();

    /// <summary>True once the Unit/Length fields have rendered (i.e. the override checkbox is checked).</summary>
    public Task<bool> IsNoticePeriodOverrideFieldsVisibleAsync() =>
        NoticePeriodOverrideRow.IsVisibleAsync();

    /// <summary>Selects a value ("Weeks" or "Months") from the notice period override's Unit dropdown. Only present once the override checkbox is checked.</summary>
    public Task SelectNoticePeriodUnitAsync(string unitLabel) =>
        DropDownSelector.SelectAsync(page, NoticePeriodOverrideRow, unitLabel);

    /// <summary>Returns the currently displayed value of the notice period override's Unit dropdown.</summary>
    public async Task<string> GetNoticePeriodUnitTextAsync()
    {
        var combobox = NoticePeriodOverrideRow.Locator("span[role='combobox']").First;
        return (await combobox.Locator("input").InputValueAsync()).Trim();
    }

    /// <summary>Sets the notice period override's Length numeric field. Only present once the override checkbox is checked.</summary>
    public Task FillNoticePeriodLengthAsync(int length) =>
        NoticePeriodOverrideRow.Locator("input.e-numerictextbox").First.FillAsync(length.ToString());

    /// <summary>Returns the current value of the notice period override's Length numeric field.</summary>
    public async Task<int> GetNoticePeriodLengthAsync()
    {
        var value = await NoticePeriodOverrideRow.Locator("input.e-numerictextbox").First.InputValueAsync();
        return int.Parse(value);
    }

    /// <summary>
    /// The read-only "Notice source" summary card (a small &lt;dl&gt;) sitting alongside the
    /// override toggle — shows the resolved Source ("Employee"/"Position Profile"/"Company
    /// Default") and Notice Period (length + unit), reflecting EffectiveNoticePeriodResolver's
    /// server-side resolution regardless of whether this employee's own override is set.
    /// </summary>
    private ILocator NoticeSourceSummary =>
        page.Locator(".col-md-6").Filter(new() { HasText = "Notice source" }).First;

    /// <summary>Returns the "Source" value from the Notice source summary (e.g. "Employee", "Position Profile", "Company Default").</summary>
    public async Task<string?> GetNoticeSourceLabelAsync()
    {
        var dd = NoticeSourceSummary.Locator("dd").First;
        return (await dd.TextContentAsync())?.Trim();
    }

    /// <summary>Returns the "Notice Period" value from the Notice source summary (e.g. "3 Weeks").</summary>
    public async Task<string?> GetEffectiveNoticePeriodTextAsync()
    {
        var dd = NoticeSourceSummary.Locator("dd").Nth(1);
        return (await dd.TextContentAsync())?.Trim();
    }

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
    // Tab label is "Compensation History" (renamed from "Compensation" — the separate
    // "Current Compensation" panel/card was removed entirely; the tab now shows only the
    // Compensation History card/grid or the single unified empty-state message).

    public async Task OpenCompensationTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Compensation History" }).ClickAsync();
        await page.WaitForSelectorAsync(
            "[data-testid='compensation-history-grid'], [data-testid='no-compensation-message']",
            new() { Timeout = 15_000 });
    }

    /// <summary>
    /// True if a "Current Compensation" panel/card is rendered on the Compensation History tab —
    /// expected to always be false now that panel was removed entirely; retained only so existing
    /// callers asserting its absence still compile.
    /// </summary>
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

    public Task SelectAddCompensationSalaryTypeAsync(string salaryType) =>
        DropDownSelector.SelectAsync(page, page.Locator(".add-compensation-dialog"), salaryType);

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

    // ── Promotion History Tab ───────────────────────────────────────────────────

    public async Task OpenPromotionHistoryTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Promotion History" }).ClickAsync();
        await page.WaitForSelectorAsync(
            "[data-testid='promote-employee-btn']",
            new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Returns true if the "No promotions recorded for this employee." empty state (HrEmptyState)
    /// is visible — i.e. the employee has no promotion history yet.
    /// </summary>
    public Task<bool> HasNoPromotionsMessageAsync() =>
        page.Locator(".hr-empty-state").Filter(new() { HasText = "No promotions recorded for this employee." }).IsVisibleAsync();

    /// <summary>Returns true if the promotion history grid is currently rendered (i.e. at least one promotion exists).</summary>
    public Task<bool> HasPromotionHistoryGridAsync() =>
        page.Locator(".e-grid").IsVisibleAsync();

    /// <summary>
    /// Returns the promotion history grid row whose rendered text contains
    /// <paramref name="textFragment"/> (e.g. a Reason or a position title).
    /// </summary>
    public ILocator PromotionHistoryRow(string textFragment) =>
        page.Locator(".e-grid .e-row").Filter(new() { HasText = textFragment });

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
        AuditHistoryRow(actionFragment).First.GetByText("View").ClickAsync();

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
        // EmployeeEdit.razor always renders a ".card" above the tab strip (e.g. the "Reporting
        // Chain" card, for any employee who has a manager) — waiting on a bare
        // ".card, .alert-secondary" selector resolves immediately against that pre-existing card
        // instead of EmployeeProbationTab's own async-loaded content, so callers that immediately
        // read the status badge can catch it while the tab's own spinner is still showing. Wait
        // for the spinner to clear first, then for the tab's own content specifically (the
        // period-summary progress bar, or the "no record" empty state).
        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
        await page.WaitForSelectorAsync(".progress, .alert-secondary", new() { Timeout = 15_000 });
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
        // ".card .badge" alone also matches the "Current Employee" badge in EmployeeEdit.razor's
        // Reporting Chain card, which sits above the tab strip and comes first in DOM order — for
        // any employee who has a manager, .First landed on that badge instead of the Probation
        // Record card's own status badge. Scope to the card whose header says "Probation Record"
        // specifically (see EmployeeProbationTab.razor).
        var badge = page.Locator(".card").Filter(new() { Has = page.Locator(".card-header:has-text('Probation Record')") })
            .Locator(".badge").First;
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
    public Task SelectRecordSicknessCategoryAsync(string categoryName) =>
        DropDownSelector.SelectAsync(
            page,
            page.Locator("[role='dialog'].record-sickness-dialog .col-12").Filter(new() { HasText = "Category" }).First,
            categoryName);

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
        await page.WaitForSelectorAsync(".e-grid .e-row", new() { Timeout = 10_000 });

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
        // Scoped to the card containing the progress bar, not just any ".card .badge" — the
        // Reporting Chain card (rendered above the tabs whenever the employee has a manager)
        // also has a badge ("Current Employee"), and an unscoped .First would grab that instead.
        var badge = page.Locator(".card:has(.progress) .badge").First;
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

    // ── Profile Photo Header (EmployeeProfilePhotoHeader — HR-managed photo for another employee) ──
    // Rendered near the top of the page (outside the tabs) whenever Session.CanManageEmployees
    // is true; see EmployeeEdit.razor.

    private ILocator ProfilePhotoImage => page.Locator("img.hr-profile-avatar");
    private ILocator ProfilePhotoInitials => page.Locator("span.hr-profile-avatar--initials");

    private ILocator PendingProfilePhotoCard =>
        page.Locator(".alert-info").Filter(new() { HasText = "Pending Review" }).First;

    /// <summary>
    /// Returns true if the profile photo header is showing an actual photo (an &lt;img&gt;)
    /// rather than the initials placeholder — i.e. the employee has an approved current photo.
    /// </summary>
    public Task<bool> HasProfilePhotoImageAsync() => ProfilePhotoImage.IsVisibleAsync();

    /// <summary>
    /// Returns true if the profile photo header is showing the initials placeholder — i.e. the
    /// employee has no approved current photo yet (see ProfilePhotoAvatar's fallback rendering).
    /// </summary>
    public Task<bool> HasProfilePhotoInitialsAsync() => ProfilePhotoInitials.IsVisibleAsync();

    /// <summary>
    /// HR uploads a photo directly via the "Upload / Replace Photo" button on the Employee Edit
    /// page header. Unlike self-service uploads, this writes straight to the approved/current
    /// photo table — no pending-review step. Per EmployeeProfilePhotoHeader.HandleUploaded, the
    /// dialog only closes (an EventCallback-driven re-render of the parent) after the header has
    /// already reloaded the current photo, so callers can assert on the new state immediately
    /// after this returns.
    /// </summary>
    public async Task UploadProfilePhotoDirectAsync(string filePath)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Upload / Replace Photo" }).ClickAsync();

        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Upload / Replace Profile Photo" });
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        await dialog.Locator("input[type='file']").SetInputFilesAsync(filePath);
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Upload", Exact = true }).ClickAsync();

        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }

    /// <summary>Returns true if a pending profile photo review card ("Pending Review") is visible in the header.</summary>
    public Task<bool> HasPendingProfilePhotoCardAsync() => PendingProfilePhotoCard.IsVisibleAsync();

    /// <summary>
    /// Approves the pending profile photo shown in the header's review card. Waits for the card
    /// to disappear as the signal that the header has refreshed with the newly-approved photo
    /// (ApproveAsync reloads both the pending and current photo before its final render).
    /// </summary>
    public async Task ApprovePendingProfilePhotoAsync()
    {
        var card = PendingProfilePhotoCard;
        await card.GetByRole(AriaRole.Button, new() { Name = "Approve" }).ClickAsync();
        await card.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }

    // ── Notes Tab ───────────────────────────────────────────────────────────────
    // Only rendered when Session.IsHrAdministrator (see EmployeeEdit.razor, wrapped in an
    // @if(Session.IsHrAdministrator) around the "Notes" TabHeader/TabContent, in addition to the
    // page-level Session.CanManageEmployees guard applied to the whole edit page).

    public async Task OpenNotesTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Notes" }).ClickAsync();
        await page.WaitForSelectorAsync(
            "[data-testid='add-note-btn'], [data-testid='no-notes-message']",
            new() { Timeout = 15_000 });
    }

    public Task<bool> HasNotesTabAsync() =>
        page.GetByRole(AriaRole.Tab, new() { Name = "Notes" }).IsVisibleAsync();

    public async Task ClickAddNoteAsync()
    {
        await page.Locator("[data-testid='add-note-btn']").ClickAsync();
        await page.Locator("[role='dialog'].add-employee-note-dialog").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    /// <summary>
    /// Selects a category from the Add Note dialog's Category dropdown. DropDownSelector itself
    /// confirms Blazor's ValueChanged round-trip actually committed the selection before
    /// returning — see its own doc comment.
    /// </summary>
    public Task SelectAddNoteCategoryAsync(string categoryLabel) =>
        DropDownSelector.SelectAsync(page, page.Locator(".add-employee-note-dialog"), categoryLabel);

    public Task FillAddNoteTextAsync(string text) =>
        page.Locator("[data-testid='add-note-text']").FillAsync(text);

    public Task CheckAddNoteImportantAsync() =>
        page.Locator(".add-employee-note-dialog").GetByLabel("Important").CheckAsync();

    public async Task SubmitAddNoteDialogAsync()
    {
        await page.Locator(".add-employee-note-dialog .e-footer-content button:has-text('Add')").ClickAsync();
        await page.Locator("[role='dialog'].add-employee-note-dialog").WaitForAsync(
            new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    public Task<bool> HasAddNoteDialogErrorAsync() =>
        page.Locator(".add-employee-note-dialog .alert-danger").IsVisibleAsync();

    /// <summary>Returns the notes grid row whose rendered text contains <paramref name="textFragment"/>.</summary>
    public ILocator NoteCard(string textFragment) =>
        page.Locator("[data-testid='employee-notes-grid'] .e-row").Filter(new() { HasText = textFragment });

    public async Task ClickSupersedeNoteAsync(string originalTextFragment)
    {
        await NoteCard(originalTextFragment).First
            .Locator("[data-testid='edit-note-btn']")
            .ClickAsync();
        await page.Locator("[role='dialog'].supersede-employee-note-dialog").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    public Task FillSupersedeNoteTextAsync(string text) =>
        page.Locator("[data-testid='supersede-note-text']").FillAsync(text);

    public Task SelectSupersedeNoteCategoryAsync(string categoryLabel) =>
        DropDownSelector.SelectAsync(page, page.Locator(".supersede-employee-note-dialog"), categoryLabel);

    public async Task SubmitSupersedeNoteDialogAsync()
    {
        await page.Locator(".supersede-employee-note-dialog .e-footer-content button:has-text('Save')").ClickAsync();
        await page.Locator("[role='dialog'].supersede-employee-note-dialog").WaitForAsync(
            new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    /// <summary>True if the note card containing <paramref name="textFragment"/> shows the "Superseded" badge.</summary>
    public Task<bool> NoteCardHasSupersededBadgeAsync(string textFragment) =>
        NoteCard(textFragment).First.Locator("[data-testid='note-superseded-badge']").IsVisibleAsync();

    /// <summary>True if the note card containing <paramref name="textFragment"/> shows the "Important" badge.</summary>
    public Task<bool> NoteCardHasImportantBadgeAsync(string textFragment) =>
        NoteCard(textFragment).First.Locator("[data-testid='note-important-badge']").IsVisibleAsync();

    /// <summary>Returns the bounding-box Y position of the note card containing <paramref name="textFragment"/> — used to assert relative ordering (important notes pinned above standard notes).</summary>
    public async Task<float?> GetNoteCardYPositionAsync(string textFragment)
    {
        var box = await NoteCard(textFragment).First.BoundingBoxAsync();
        return box?.Y;
    }
}
