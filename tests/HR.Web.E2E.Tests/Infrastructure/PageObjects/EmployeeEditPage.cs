using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class EmployeeEditPage(IPage page, string baseUrl)
{
    internal static readonly IReadOnlySet<string> FlatSingleSectionGroups = new HashSet<string> { "Assets" };

    internal static readonly IReadOnlyDictionary<string, string> SectionGroups = new Dictionary<string, string>
    {
        ["Details"] = "Overview",
        ["Employment"] = "Overview",
        ["Probation"] = "Overview",
        ["Emergency Contacts"] = "Overview",
        ["Compensation History"] = "Career & Pay",
        ["Promotion History"] = "Career & Pay",
        ["Leave"] = "Time Off",
        ["Sickness"] = "Time Off",
        ["Tasks"] = "Tasks & Records",
        ["Documents"] = "Tasks & Records",
        ["Acknowledgement History"] = "Tasks & Records",
        ["Onboarding"] = "Tasks & Records",
        ["Offboarding"] = "Tasks & Records",
        ["Leaving"] = "Tasks & Records",
        ["Leaving & Offboarding"] = "Tasks & Records",
        ["Assets"] = "Assets",
        ["Timeline"] = "Activity",
        ["Notes"] = "Activity",
        ["Audit"] = "Activity",
    };

    private static readonly IReadOnlyDictionary<string, string> SectionDisplayNames = new Dictionary<string, string>
    {
        ["Leaving"] = "Leaving & Offboarding",
        ["Offboarding"] = "Leaving & Offboarding",
    };

    private static string DisplayNameOf(string sectionName) =>
        SectionDisplayNames.TryGetValue(sectionName, out var display) ? display : sectionName;

    public static async Task NavigateToSectionAsync(IPage page, string sectionName)
    {
        if (!SectionGroups.TryGetValue(sectionName, out var groupName))
            throw new ArgumentException($"Unknown employee-profile section '{sectionName}'.", nameof(sectionName));

        var groupTab = page.Locator(".employee-profile-groups > .e-tab-header")
            .GetByRole(AriaRole.Tab, new() { Name = groupName, Exact = true });
        await groupTab.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await groupTab.ClickAsync();

        if (FlatSingleSectionGroups.Contains(groupName))
            return;

        var sectionTab = page.Locator(".employee-profile-sections > .e-tab-header")
            .GetByRole(AriaRole.Tab, new() { Name = DisplayNameOf(sectionName), Exact = true });
        await sectionTab.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await sectionTab.ClickAsync();
    }

    private Task OpenSectionAsync(string sectionName) => NavigateToSectionAsync(page, sectionName);

    public static async Task SelectOwningGroupAsync(IPage page, string sectionName)
    {
        if (!SectionGroups.TryGetValue(sectionName, out var groupName))
            throw new ArgumentException($"Unknown employee-profile section '{sectionName}'.", nameof(sectionName));

        var groupTab = page.Locator(".employee-profile-groups > .e-tab-header")
            .GetByRole(AriaRole.Tab, new() { Name = groupName, Exact = true });
        await groupTab.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await groupTab.ClickAsync();
    }

    public static ILocator SectionTab(IPage page, string sectionName) =>
        page.Locator(".employee-profile-sections > .e-tab-header")
            .GetByRole(AriaRole.Tab, new() { Name = DisplayNameOf(sectionName), Exact = true });

    public static async Task<bool> IsSectionTabPresentAsync(IPage page, string sectionName)
    {
        await SelectOwningGroupAsync(page, sectionName);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            if (await SectionTab(page, sectionName).IsVisibleAsync())
                return true;
            if (DateTime.UtcNow >= deadline)
                return false;
            await page.WaitForTimeoutAsync(250);
        }
    }

    public async Task GoToNewAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/employees/new");
        await page.WaitForSelectorAsync(".content-area span[role='combobox']", new() { Timeout = 20_000 });
    }

    public async Task GoToAsync(Guid companyId, Guid employeeId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/employees/{employeeId}");
        await page.WaitForSelectorAsync(".content-area span[role='combobox']", new() { Timeout = 20_000 });
    }

    public async Task GoToViewAsync(Guid companyId, Guid employeeId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/employees/{employeeId}/view");
        await page.WaitForSelectorAsync(".content-area span[role='combobox']", new() { Timeout = 20_000 });
    }

    public async Task GoToAsync(Guid companyId, Guid employeeId, string query)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/employees/{employeeId}?{query}");
        await page.WaitForSelectorAsync("[role='tablist']", new() { Timeout = 20_000 });
    }


    public async Task FillFirstNameAsync(string value)
    {
        await page.GetByPlaceholder("First name", new() { Exact = true }).FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillLastNameAsync(string value)
    {
        await page.GetByPlaceholder("Last name").FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillWorkEmailAsync(string value)
    {
        await page.GetByPlaceholder("work@company.com").FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillStartDateAsync(string ddMMyyyy)
    {
        var inputs = page.Locator(".e-date-wrapper input.e-input");
        await inputs.Nth(1).ClickAsync();
        await inputs.Nth(1).FillAsync(ddMMyyyy);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillDateOfBirthAsync(string ddMMyyyy)
    {
        var inputs = page.Locator(".e-date-wrapper input.e-input");
        await inputs.First.ClickAsync();
        await inputs.First.FillAsync(ddMMyyyy);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task SelectDropdownAsync(string labelText, string optionText)
    {
        await DropDownSelector.SelectAsync(page, page.Locator(".col-md-6, .col-md-4").Filter(new() { HasText = labelText }).First, optionText);

        if (labelText == "Position Profile")
        {
            await WaitForDropdownPopulatedAsync("Department");
            await WaitForDropdownPopulatedAsync("Location");
        }
    }

    public async Task WaitForDropdownPopulatedAsync(string labelText)
    {
        var input = page.Locator(".col-md-6, .col-md-4").Filter(new() { HasText = labelText }).First
            .Locator("span[role='combobox'] input").First;
        await Microsoft.Playwright.Assertions.Expect(input).Not.ToHaveValueAsync("", new() { Timeout = 10_000 });
    }


    public async Task<string?> GetEmployeeStatusBadgeTextAsync()
    {
        var badge = page.Locator(".badge.rounded-pill").First;
        try
        {
            await badge.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        }
        catch (TimeoutException)
        {
            return null;
        }

        return (await badge.TextContentAsync())?.Trim();
    }


    public bool IsInViewModeUrl => page.Url.Contains("/view", StringComparison.OrdinalIgnoreCase);

    public async Task<bool> IsEditDetailsButtonVisibleAsync()
    {
        try
        {
            await page.Locator("[data-testid='edit-details-button']").WaitForAsync(
                new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task ClickEditDetailsButtonAsync()
    {
        var button = page.Locator("[data-testid='edit-details-button']");

        try
        {
            await button.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 20_000 });
        }
        catch (TimeoutException)
        {
            throw new Exception(
                "Timed out waiting for the 'Edit details' button to appear. " +
                $"Current URL: {page.Url} (IsViewMode expected — url should contain '/view'). " +
                "The button only renders when IsViewMode is true and the caller can manage employees — " +
                "check whether navigation actually landed on the view route.");
        }

        await button.ClickAsync();
        await page.WaitForURLAsync(url => !url.Contains("/view", StringComparison.OrdinalIgnoreCase), new() { Timeout = 40_000 });
        await page.WaitForSelectorAsync(".content-area span[role='combobox']", new() { Timeout = 20_000 });
    }

    public async Task<bool> IsBackToEmployeesButtonVisibleAsync()
    {
        try
        {
            await page.GetByRole(AriaRole.Button, new() { Name = "Back to employees" }).WaitForAsync(
                new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task ClickBackToEmployeesButtonAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Back to employees" }).ClickAsync();
        await page.WaitForURLAsync("**/employees", new() { Timeout = 40_000 });
    }

    public Task<bool> IsStickyActionBarVisibleAsync() =>
        page.Locator(".employee-edit-sticky-bar").IsVisibleAsync();

    public async Task<string?> GetSaveSuccessBannerTextAsync()
    {
        var banner = page.Locator("[role='status'][aria-live='polite'].alert-success");
        return await banner.IsVisibleAsync() ? (await banner.TextContentAsync())?.Trim() : null;
    }

    // ── Optimistic-concurrency conflict banner (Ticket 2) ─────────────────────
    // EmployeeEdit.razor renders the shared <SaveConflictBanner> component, whose root markup is a
    // single `div.alert.alert-warning.save-conflict-banner[role='alert']` containing a
    // "Reload latest values" Syncfusion button — distinct from the generic red `.alert-danger`
    // GlobalError alert. Scope on the component's own `.save-conflict-banner` class (+ the
    // role='alert' attribute) rather than the shared Bootstrap `.alert-warning` class alone, and
    // additionally require the "Reload latest values" action so an unrelated warning alert can
    // never satisfy strict mode. Match on structure, not text: on a real 409 the razor overrides
    // the component's default Message with its own friendly copy, so a text filter would be brittle.
    private ILocator ConcurrencyWarningBanner =>
        page.Locator(".save-conflict-banner[role='alert']")
            .Filter(new() { Has = page.GetByRole(AriaRole.Button, new() { Name = "Reload latest values" }) });

    /// <summary>
    /// Clicks the single page-level Save and waits for the optimistic-concurrency warning banner
    /// to appear — i.e. the save was rejected because the employee changed since it was loaded.
    /// Does NOT expect (or wait for) a navigation, since a conflicted save stays on the edit page.
    /// </summary>
    public async Task ClickSaveExpectingConflictAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await page.WaitForSpinnerToClearAsync();
        await ConcurrencyWarningBanner.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 20_000 });
    }

    public Task<bool> IsConcurrencyWarningVisibleAsync() =>
        ConcurrencyWarningBanner.IsVisibleAsync();

    public async Task ClickReloadLatestValuesAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Reload latest values" }).ClickAsync();
        await ConcurrencyWarningBanner.WaitForAsync(
            new() { State = WaitForSelectorState.Hidden, Timeout = 20_000 });
    }

    public async Task MakeDetailsFormInvalidAsync()
    {
        var lastName = page.GetByLabel("Last Name").First;
        await lastName.ClickAsync();
        await lastName.FillAsync("");
        await page.Keyboard.PressAsync("Tab");
        await page.Locator(".validation-message").First.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }


    public async Task<bool> IsMoreActionsMenuVisibleAsync()
    {
        try
        {
            await page.GetByRole(AriaRole.Button, new() { Name = "More actions" })
                .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public Task OpenMoreActionsMenuAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "More actions" }).ClickAsync();

    public async Task ClickViewOrganisationChartMenuItemAsync()
    {
        await OpenMoreActionsMenuAsync();
        await page.Locator("#org-chart").ClickAsync();
        await page.WaitForURLAsync(new System.Text.RegularExpressions.Regex(@"/organisation-chart\?employeeId="), new() { Timeout = 15_000 });
    }

    public async Task<bool> HasStartOffboardingMenuItemAsync()
    {
        if (!await IsMoreActionsMenuVisibleAsync())
            return false;

        await OpenMoreActionsMenuAsync();
        bool visible;
        try
        {
            await page.Locator("#start-offboarding")
                .WaitForAsync(new() { Timeout = 3_000 });
            visible = true;
        }
        catch (TimeoutException)
        {
            visible = false;
        }

        await page.Keyboard.PressAsync("Escape");
        return visible;
    }

    public async Task ClickStartOffboardingMenuItemAsync()
    {
        await OpenMoreActionsMenuAsync();
        await page.Locator("#start-offboarding").ClickAsync();
    }


    public async Task<bool> IsTextFieldReadOnlyAsync(string fieldLabel) =>
        await page.GetByLabel(fieldLabel).First.GetAttributeAsync("readonly") is not null;

    public Task<string> GetTextFieldValueAsync(string fieldLabel) =>
        page.GetByLabel(fieldLabel).First.InputValueAsync();

    public Task FillTextFieldByIdAsync(string fieldLabel, string value) =>
        page.GetByLabel(fieldLabel).First.FillAsync(value);

    private ILocator PreferredNameField => page.GetByPlaceholder("Defaults to first name");

    private async Task SwitchToDetailsSectionAsync()
    {
        await NavigateToSectionAsync(page, "Details");
        await PreferredNameField.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    public async Task FillPreferredNameAsync(string value)
    {
        await SwitchToDetailsSectionAsync();
        await PreferredNameField.FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task<string> GetPreferredNameValueAsync()
    {
        await SwitchToDetailsSectionAsync();
        return await PreferredNameField.InputValueAsync();
    }

    public async Task<bool> HasRequiredFieldsNoteAsync()
    {
        var note = page.Locator("p").Filter(new() { HasText = "Fields marked" }).Filter(new() { HasText = "are required" }).First;
        try
        {
            await note.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }


    public async Task OpenEmploymentTabAsync()
    {
        await OpenSectionAsync("Employment");
        await page.WaitForSelectorAsync(".card-header:has-text('Employment Details')", new() { Timeout = 15_000 });

        // The heading above is on the FIRST card of this tab; every combobox further down still
        // needs Syncfusion's JS interop to attach before it's genuinely click-ready, and the very
        // first popup opened on a freshly-loaded page pays a further, much larger one-time
        // cold-start cost on top of that. DropDownSelector itself detects and sizes for both
        // automatically (see its own remarks) — but that just means the FIRST dropdown any caller
        // happens to touch on this tab pays the cold cost, whichever one it is (confirmed: this
        // tab's Position Profile field hitting it in CreateEmployeeTests, after previously being
        // reliable, once this warm-up was removed during an earlier consolidation pass). Paying
        // that cost once, right here, up front, means every real selection a caller makes
        // afterward — Manager, Position Profile, Department, whatever order the test uses — lands
        // on the fast "warm" path instead of each independently risking being the unlucky first
        // one. Manager is the natural choice: last in DOM order, so warming it up implies every
        // earlier combobox on this tab already had time to attach too.
        // A plain click + Escape, not DropDownSelector.SelectAsync — that method deliberately
        // no-ops when the combobox's current value already matches the target text (correct for
        // real selections, wrong here: a brand-new employee's Manager already reads "No Manager",
        // which is exactly the common case this warm-up most needs to cover, so skipping on
        // already-matching text would skip the warm-up for it entirely).
        var managerCombobox = page.Locator(".col-md-4, .col-12")
            .Filter(new() { HasText = "Manager" })
            .First
            .Locator("span[role='combobox']")
            .First;
        try
        {
            await managerCombobox.ClickAsync(new() { Timeout = 60_000 });
            await page.Keyboard.PressAsync("Escape");
            await page.WaitForTimeoutAsync(250);
        }
        catch (TimeoutException)
        {
        }
    }

    public async Task SwitchToEmploymentSectionAsync()
    {
        await NavigateToSectionAsync(page, "Employment");
        await page.WaitForSelectorAsync(".card-header:has-text('Employment Details')", new() { Timeout = 15_000 });
    }

    private ILocator EmploymentNotesField =>
        page.GetByPlaceholder("Optional internal notes visible to HR only");

    public async Task FillEmploymentNotesAsync(string value)
    {
        await SwitchToEmploymentSectionAsync();
        await EmploymentNotesField.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await EmploymentNotesField.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.PressAsync("Delete");
        await page.WaitForTimeoutAsync(150);
        if (value.Length > 0)
            await EmploymentNotesField.PressSequentiallyAsync(value, new() { Delay = 15 });
        await page.Keyboard.PressAsync("Tab");
        await page.WaitForTimeoutAsync(300);
    }

    public async Task<string> GetEmploymentNotesValueAsync()
    {
        await SwitchToEmploymentSectionAsync();
        await EmploymentNotesField.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        return await EmploymentNotesField.InputValueAsync();
    }

    public async Task<string?> GetEmploymentTabReadOnlyFieldAsync(string labelText)
    {
        var row = page.Locator("table.table-sm tr").Filter(new() { HasText = labelText }).First;
        if (await row.CountAsync() == 0) return null;
        var value = row.Locator("td").First;
        return await value.IsVisibleAsync() ? (await value.TextContentAsync())?.Trim() : null;
    }

    public async Task<IReadOnlyList<string>> GetEmploymentTabCardHeadingsAsync()
    {
        var headers = await page.Locator(".card-header h5").AllAsync();
        var result = new List<string>();
        foreach (var header in headers)
            result.Add((await header.TextContentAsync())?.Trim() ?? "");
        return result;
    }

    public async Task SelectManagerAsync(string managerNameFragment)
    {
        var managerGroup = page.Locator(".col-md-4, .col-12")
            .Filter(new() { HasText = "Manager" })
            .First;
        await DropDownSelector.SelectAsync(page, managerGroup, managerNameFragment);
    }

    public async Task ClearManagerAsync()
    {
        var managerGroup = page.Locator(".col-md-4, .col-12")
            .Filter(new() { HasText = "Manager" })
            .First;
        await DropDownSelector.SelectAsync(page, managerGroup, "No Manager");
    }

    public async Task ClickSaveChangesAsync()
    {
        var urlBeforeSave = page.Url;

        await page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await page.WaitForSpinnerToClearAsync();

        var errorBanner = page.Locator(".alert-danger").First;
        if (await errorBanner.IsVisibleAsync())
        {
            var message = (await errorBanner.TextContentAsync())?.Trim();
            throw new Exception($"Save failed: {message}");
        }

        try
        {
            await page.WaitForURLAsync(url => url.ToString() != urlBeforeSave, new() { Timeout = 5_000 });
        }
        catch (TimeoutException)
        {
        }
    }


    public async Task SaveNewEmployeeAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        try
        {
            await page.WaitForSelectorAsync(".alert-danger", new() { Timeout = 2_000 });
            var message = (await page.Locator(".alert-danger").First.TextContentAsync())?.Trim();
            throw new Exception($"Save failed: {message}");
        }
        catch (TimeoutException)
        {
        }

        await page.WaitForURLAsync("**/employees", new() { Timeout = 60_000 });
        await page.WaitForSelectorAsync(".e-grid .e-row, .e-grid .e-emptyrow", new() { Timeout = 20_000 });
    }

    public async Task<bool> HasProbationSummaryAsync() =>
        await page.Locator("[data-testid='probation-summary']").IsVisibleAsync();

    public async Task<bool> HasPositionProfileDefaultsSummaryAsync() =>
        await page.Locator("[data-testid='position-profile-defaults-summary']").IsVisibleAsync();

    public async Task<string?> GetSelectedDepartmentTextAsync()
    {
        var group = page.Locator(".col-md-4").Filter(new() { HasText = "Department" }).First;
        return await group.Locator(".e-input-group input").First.InputValueAsync();
    }

    public async Task<string?> GetSelectedLocationTextAsync()
    {
        var group = page.Locator(".col-md-4").Filter(new() { HasText = "Location" }).First;
        return await group.Locator(".e-input-group input").First.InputValueAsync();
    }

    public async Task<string?> GetSelectedManagerTextAsync()
    {
        var group = page.Locator(".col-md-4, .col-12").Filter(new() { HasText = "Manager" }).First;
        return await group.Locator(".e-input-group input").First.InputValueAsync();
    }

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

    public async Task<bool> HasValidationMessageAsync(string messageText) =>
        await page.Locator(".validation-message").Filter(new() { HasText = messageText }).First.IsVisibleAsync();

    public async Task FillEmployeeNumberAsync(string value)
    {
        var field = page.GetByPlaceholder("e.g. EMP-001");
        var autoAssignedMessage = page.Locator("p")
            .Filter(new() { HasText = "An employee number will be assigned automatically" });

        // Wait for whichever of the two mutually exclusive renders the form actually settled on —
        // the input (Manual mode) or the "assigned automatically" message (Automatic mode; New
        // Employee form only) — rather than polling the input's visibility for a fixed 10s and
        // treating "not seen yet" as "Automatic". Filling whenever the input is present is always
        // safe: CreateEmployeeHandler uses a supplied number in both modes. Only positively seeing
        // the Automatic message skips the fill.
        //
        // NB: Acme's numbering mode is deliberately never mutated by any E2E test any more (the
        // tests that need Manual mode use Beta Corp under the HrSettingsSerial gate — see
        // CreateEmployeeTests) — flipping it mid-run is what made concurrent Acme employee
        // creation fail with "Save failed: Employee number is required." (form rendered in
        // Automatic, company switched to Manual before Save).
        await field.Or(autoAssignedMessage).First.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 20_000 });

        if (await field.IsVisibleAsync())
        {
            await field.FillAsync(value);
            await page.Keyboard.PressAsync("Tab");
        }
    }

    public async Task<string> GetEmployeeNumberFieldValueAsync()
    {
        await NavigateToSectionAsync(page, "Employment");
        var field = page.GetByPlaceholder("e.g. EMP-001");
        await field.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        return await field.InputValueAsync();
    }

    public async Task<bool> IsEmployeeNumberInputVisibleAsync()
    {
        try
        {
            await Assertions.Expect(page.GetByPlaceholder("e.g. EMP-001")).ToBeVisibleAsync(new() { Timeout = 10_000 });
            return true;
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    public async Task<bool> HasEmployeeNumberAutoAssignedMessageAsync()
    {
        try
        {
            await Assertions.Expect(page.Locator("p").Filter(new() { HasText = "An employee number will be assigned automatically" }).First)
                .ToBeVisibleAsync(new() { Timeout = 10_000 });
            return true;
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    public async Task<string?> GetEmployeeNumberHeaderTextAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var spans = await page.Locator("span.text-muted").AllAsync();
            foreach (var span in spans)
            {
                var text = (await span.TextContentAsync())?.Trim();
                if (text is not null && text.StartsWith('#'))
                    return text;
            }
            await page.WaitForTimeoutAsync(200);
        }
        return null;
    }


    private ILocator NoticePeriodOverrideRow =>
        page.Locator(".e-checkbox-wrapper")
            .Filter(new() { HasText = "Override notice period" })
            .Locator("xpath=following-sibling::div[contains(@class,'row')]");

    public async Task SetOverrideNoticePeriodAsync(bool overrideEnabled)
    {
        var checkbox = page.GetByLabel("Override notice period");
        var isChecked = await checkbox.IsCheckedAsync();
        if (overrideEnabled && !isChecked)
        {
            await checkbox.CheckAsync();
            await NoticePeriodOverrideRow.WaitForAsync(new() { Timeout = 10_000 });

            try
            {
                await Assertions.Expect(NoticePeriodOverrideRow.Locator("input.e-numerictextbox").First)
                    .ToBeEnabledAsync(new() { Timeout = 90_000 });
            }
            catch (PlaywrightException)
            {
            }
        }
        if (!overrideEnabled && isChecked)
        {
            await checkbox.UncheckAsync();
            await NoticePeriodOverrideRow.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
        }
    }

    public Task<bool> IsOverrideNoticePeriodCheckedAsync() =>
        page.GetByLabel("Override notice period").IsCheckedAsync();

    public Task<bool> IsNoticePeriodOverrideFieldsVisibleAsync() =>
        NoticePeriodOverrideRow.IsVisibleAsync();

    public Task SelectNoticePeriodUnitAsync(string unitLabel) =>
        DropDownSelector.SelectAsync(page, NoticePeriodOverrideRow, unitLabel);

    public async Task<string> GetNoticePeriodUnitTextAsync()
    {
        var combobox = NoticePeriodOverrideRow.Locator("span[role='combobox']").First;
        return (await combobox.Locator("input").InputValueAsync()).Trim();
    }

    public Task FillNoticePeriodLengthAsync(int length) =>
        TypeIntoNumericInputAsync(NoticePeriodOverrideRow.Locator("input.e-numerictextbox").First, length.ToString());

    public async Task<int> GetNoticePeriodLengthAsync()
    {
        var value = await NoticePeriodOverrideRow.Locator("input.e-numerictextbox").First.InputValueAsync();
        return int.Parse(value);
    }

    private ILocator NoticeSourceSummary =>
        page.Locator(".col-md-6").Filter(new() { HasText = "Notice source" }).First;

    public async Task<string?> GetNoticeSourceLabelAsync()
    {
        var dd = NoticeSourceSummary.Locator("dd").First;
        return (await dd.TextContentAsync())?.Trim();
    }

    public async Task<string?> GetEffectiveNoticePeriodTextAsync()
    {
        var dd = NoticeSourceSummary.Locator("dd").Nth(1);
        return (await dd.TextContentAsync())?.Trim();
    }


    private ILocator UnsavedChangesDialog => page.Locator("[role='dialog']:has-text('Unsaved Changes')");

    public Task ClickCloseAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();

    public Task<bool> IsUnsavedChangesDialogVisibleAsync() =>
        UnsavedChangesDialog.WaitUntilVisibleAsync();

    public async Task ConfirmDiscardChangesAsync()
    {
        await UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Discard Changes" }).ClickAsync();
        await page.WaitForURLAsync("**/employees", new() { Timeout = 40_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public async Task ConfirmSaveFromUnsavedChangesDialogAsync()
    {
        await UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await page.WaitForURLAsync("**/employees", new() { Timeout = 40_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public Task CancelUnsavedChangesDialogAsync() =>
        UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();


    public async Task OpenCompensationTabAsync()
    {
        await OpenSectionAsync("Compensation History");
        await page.WaitForSelectorAsync(
            "[data-testid='compensation-history-grid'], [data-testid='no-compensation-message']",
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
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await input.ClickAsync();
            await input.FillAsync("");
            await input.FillAsync(ddMMyyyy);
            await page.Keyboard.PressAsync("Tab");
            await page.WaitForTimeoutAsync(200);

            var actual = await input.InputValueAsync();
            if (!string.IsNullOrWhiteSpace(actual) && actual.Contains(ddMMyyyy[^4..]))
                return;

            if (attempt < 3)
                await page.WaitForTimeoutAsync(250);
        }
    }

    public Task SelectAddCompensationSalaryTypeAsync(string salaryType) =>
        DropDownSelector.SelectAsync(page, page.Locator(".add-compensation-dialog"), salaryType);

    public Task FillAddCompensationSalaryAsync(string value) =>
        FillNumericAndVerifyAsync(page.Locator(".add-compensation-dialog input.e-numerictextbox").First, value, decimal.Parse(value));

    public async Task FillAddCompensationCurrencyAsync(string value)
    {
        var input = page.Locator(".add-compensation-dialog").GetByPlaceholder("e.g. GBP");
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await input.FillAsync(value);
            await page.Keyboard.PressAsync("Tab");
            await page.WaitForTimeoutAsync(200);

            if ((await input.InputValueAsync()).Trim().Equals(value, StringComparison.OrdinalIgnoreCase))
                return;

            if (attempt < 3)
                await page.WaitForTimeoutAsync(250);
        }
    }

    public async Task SubmitAddCompensationDialogAsync()
    {
        var dialog = page.Locator("[role='dialog'].add-compensation-dialog");
        var addButton = page.Locator(".add-compensation-dialog .e-footer-content button:has-text('Add')");

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            if (await dialog.CountAsync() == 0)
                break;

            await addButton.ClickAsync();
            try
            {
                await dialog.WaitForAsync(new()
                {
                    State = WaitForSelectorState.Hidden,
                    Timeout = attempt < 5 ? 8_000 : 30_000,
                });
                break;
            }
            catch (TimeoutException) when (attempt < 5)
            {
                if (await HasAddCompensationDialogErrorAsync())
                {
                    var message = (await page.Locator(".add-compensation-dialog .alert-danger").First.TextContentAsync())?.Trim() ?? "";

                    if (message.Contains("correct the highlighted", StringComparison.OrdinalIgnoreCase))
                    {
                        await ReblurAddCompensationFieldsAsync();
                        await page.WaitForTimeoutAsync(400);
                        continue;
                    }

                    throw new InvalidOperationException($"Add Compensation dialog rejected the submit: {message}");
                }

                await page.WaitForTimeoutAsync(400);
            }
        }

        if (await dialog.CountAsync() > 0 && await HasAddCompensationDialogErrorAsync())
        {
            var message = (await page.Locator(".add-compensation-dialog .alert-danger").First.TextContentAsync())?.Trim();
            throw new InvalidOperationException($"Add Compensation dialog never accepted the submit: {message}");
        }

        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        await page.WaitForTimeoutAsync(300);
    }

    public Task<bool> HasAddCompensationDialogErrorAsync() =>
        page.Locator(".add-compensation-dialog .alert-danger").IsVisibleAsync();

    private async Task ReblurAddCompensationFieldsAsync()
    {
        var fields = new[]
        {
            page.Locator(".add-compensation-dialog .e-date-wrapper input.e-input").First,
            page.Locator(".add-compensation-dialog input.e-numerictextbox").First,
            page.Locator(".add-compensation-dialog").GetByPlaceholder("e.g. GBP"),
        };

        foreach (var field in fields)
        {
            if (await field.CountAsync() == 0)
                continue;
            await field.ClickAsync();
            await page.Keyboard.PressAsync("Tab");
            await page.WaitForTimeoutAsync(150);
        }
    }

    public ILocator CompensationHistoryRow(string effectiveFromFragment) =>
        page.Locator("[data-testid='compensation-history-grid'] .e-row").Filter(new() { HasText = effectiveFromFragment });

    public Task ClickEditCompensationRowAsync(string effectiveFromFragment) =>
        CompensationHistoryRow(effectiveFromFragment).First.GetByTitle("Edit").ClickAsync();

    public Task ClickDeleteCompensationRowAsync(string effectiveFromFragment) =>
        CompensationHistoryRow(effectiveFromFragment).First.GetByTitle("Delete").ClickAsync();

    public async Task ConfirmDeleteCompensationAsync()
    {
        var yesButton = page.Locator("[data-testid='compensation-history-grid']").GetByRole(AriaRole.Button, new() { Name = "Yes" });
        await yesButton.ClickAsync();
        await yesButton.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    public async Task FillEditCompensationSalaryAsync(string value)
    {
        await page.Locator("[role='dialog'].edit-future-compensation-dialog").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await FillNumericAndVerifyAsync(page.Locator(".edit-future-compensation-dialog input.e-numerictextbox").First, value, decimal.Parse(value));
    }

    public async Task SubmitEditCompensationDialogAsync()
    {
        await page.Locator(".edit-future-compensation-dialog .e-footer-content button:has-text('Save')").ClickAsync();
        await page.Locator("[role='dialog'].edit-future-compensation-dialog").WaitForAsync(
            new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });

        await page.WaitForFunctionAsync(
            "!document.querySelector('.spinner-border') || !document.querySelector('.spinner-border').offsetParent",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        await page.WaitForTimeoutAsync(300);
    }

    private async Task TypeIntoNumericInputAsync(ILocator input, string value)
    {
        await input.WaitForAsync(new() { State = WaitForSelectorState.Attached });
        await Assertions.Expect(input).ToBeEnabledAsync(new() { Timeout = 90_000 });
        await input.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.PressAsync("Delete");
        await page.WaitForTimeoutAsync(150);
        if (value.Length > 0)
            await input.PressSequentiallyAsync(value, new() { Delay = 30 });
        await page.Keyboard.PressAsync("Tab");
    }

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


    public async Task OpenPromotionHistoryTabAsync()
    {
        await OpenSectionAsync("Promotion History");
        await page.WaitForSelectorAsync(
            "[data-testid='promote-employee-btn']",
            new() { Timeout = 15_000 });
    }

    public Task<bool> HasNoPromotionsMessageAsync() =>
        page.Locator(".hr-empty-state").Filter(new() { HasText = "No promotions recorded for this employee." }).IsVisibleAsync();

    public Task<bool> HasPromotionHistoryGridAsync() =>
        page.Locator(".e-grid").IsVisibleAsync();

    public ILocator PromotionHistoryRow(string textFragment) =>
        page.Locator(".e-grid .e-row").Filter(new() { HasText = textFragment });


    public async Task OpenAuditTabAsync()
    {
        await OpenSectionAsync("Audit");
        await page.WaitForSelectorAsync(
            "[data-testid='audit-history-grid'], .alert-secondary",
            new() { Timeout = 15_000 });

        if (await page.Locator("[data-testid='audit-history-grid']").IsVisibleAsync())
        {
            await page.WaitForSelectorAsync(
                "[data-testid='audit-history-grid'] .e-row, [data-testid='audit-history-grid'] .e-emptyrow",
                new() { Timeout = 15_000 });
        }
    }

    public ILocator AuditHistoryRow(string actionFragment) =>
        page.Locator("[data-testid='audit-history-grid'] .e-row").Filter(new() { HasText = actionFragment });

    public async Task ClickViewAuditRowAsync(string actionFragment)
    {
        await AuditHistoryRow(actionFragment).First.GetByText("View").ClickAsync();
        await page.Locator("[role='dialog'].audit-history-detail-dialog").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

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


    public async Task OpenProbationTabAsync()
    {
        await OpenSectionAsync("Probation");
        await page.WaitForSpinnerToClearAsync();
        await page.WaitForSelectorAsync(".progress, .alert-secondary", new() { Timeout = 15_000 });
    }

    public async Task<bool> HasProbationPeriodSummaryPanelAsync() =>
        await page.Locator(".progress").IsVisibleAsync();

    public async Task<bool> HasProbationReviewsGridAsync() =>
        await page.Locator(".e-grid").IsVisibleAsync();

    public async Task<string?> GetProbationStatusBadgeTextAsync()
    {
        var badge = page.Locator(".card").Filter(new() { Has = page.Locator(".card-header:has-text('Probation Record')") })
            .Locator(".badge").First;
        return await badge.IsVisibleAsync() ? (await badge.TextContentAsync())?.Trim() : null;
    }

    public async Task<string?> GetReviewStatusInGridAsync(string reviewTypeFragment)
    {
        await page.WaitForSelectorAsync(".e-grid .e-row", new() { Timeout = 10_000 });

        var rows = await page.Locator(".e-grid .e-row").AllAsync();
        foreach (var row in rows)
        {
            var text = await row.TextContentAsync();
            if (text?.Contains(reviewTypeFragment, StringComparison.OrdinalIgnoreCase) != true)
                continue;

            var badge = row.Locator(".badge").First;
            if (await badge.IsVisibleAsync())
                return (await badge.TextContentAsync())?.Trim();
        }

        return null;
    }


    public async Task OpenTasksTabAsync()
    {
        await OpenSectionAsync("Tasks");
        await page.WaitForSelectorAsync(".e-grid, .task-cell, p", new() { Timeout = 15_000 });
    }

    public async Task ClickTaskAsync(Guid taskId)
    {
        var row = page.Locator($"[data-testid='task-view-btn-{taskId}']");
        await row.WaitForAsync(new() { Timeout = 15_000 });
        await row.ClickAsync();
        await page.WaitForSelectorAsync(".task-view-dialog", new() { Timeout = 15_000 });
    }


    public async Task OpenSicknessTabAsync()
    {
        await OpenSectionAsync("Sickness");
        await page.WaitForSpinnerToClearAsync();
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 15_000 });
    }

    public Task<bool> HasSicknessGridAsync() =>
        page.Locator(".e-grid").First.WaitUntilVisibleAsync();

    public async Task OpenRecordSicknessDialogAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Record Sickness" }).ClickAsync();
        await page.WaitForSelectorAsync("[role='dialog'].record-sickness-dialog", new() { Timeout = 10_000 });
    }

    public Task SelectRecordSicknessCategoryAsync(string categoryName) =>
        DropDownSelector.SelectAsync(
            page,
            page.Locator("[role='dialog'].record-sickness-dialog .col-12").Filter(new() { HasText = "Category" }).First,
            categoryName);

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

    public async Task<string?> GetSicknessStatusBadgeForStartDateAsync(string startDateddMMMyyyy)
    {
        await page.WaitForSelectorAsync(".e-grid .e-row", new() { Timeout = 10_000 });

        var row = page.Locator(".e-grid .e-row")
            .Filter(new() { HasText = startDateddMMMyyyy })
            .First;

        var badge = row.Locator(".badge").First;
        return await badge.IsVisibleAsync() ? (await badge.TextContentAsync())?.Trim() : null;
    }

    public Task ExpectSicknessStatusForStartDateAsync(string startDateddMMMyyyy, string status) =>
        Assertions.Expect(
            page.Locator(".e-grid .e-row")
                .Filter(new() { HasText = startDateddMMMyyyy })
                .First.Locator(".badge").First)
            .ToHaveTextAsync(status, new() { Timeout = 15_000 });

    public async Task StartCloseSicknessRecordAsync(string startDateddMMMyyyy)
    {
        await page.WaitForSelectorAsync(".e-grid .e-row", new() { Timeout = 10_000 });

        var row = page.Locator(".e-grid .e-row")
            .Filter(new() { HasText = startDateddMMMyyyy })
            .First;
        await row.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true }).ClickAsync();
        await page.WaitForSelectorAsync("[role='dialog'].close-sickness-record-dialog", new() { Timeout = 10_000 });
    }

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


    public async Task OpenOnboardingTabAsync()
    {
        await OpenSectionAsync("Onboarding");
        await page.WaitForSelectorAsync(".progress, .hr-empty-state", new() { Timeout = 15_000 });
    }

    public async Task<bool> HasOnboardingProgressPanelAsync() =>
        await page.Locator(".progress").IsVisibleAsync();

    public async Task<bool> HasOnboardingChecklistAsync() =>
        await page.Locator(".card-header:has-text('Onboarding Checklist')").IsVisibleAsync();

    public async Task<bool> HasOnboardingTimelineAsync() =>
        await page.Locator(".card-header:has-text('Onboarding Timeline')").IsVisibleAsync();

    public async Task<string?> GetOnboardingStatusBadgeTextAsync()
    {
        var badge = page.Locator(".card:has(.progress) .badge").First;
        return await badge.IsVisibleAsync() ? (await badge.TextContentAsync())?.Trim() : null;
    }

    public async Task<int> GetOnboardingProgressPercentAsync()
    {
        var bar = page.Locator(".progress .progress-bar");
        var value = await bar.GetAttributeAsync("aria-valuenow");
        return int.TryParse(value, out var percent) ? percent : 0;
    }

    public async Task<string?> GetOnboardingChecklistTaskStatusAsync(string taskTitleFragment)
    {
        var checklistCard = page.Locator(".card").Filter(new() { HasText = "Onboarding Checklist" }).First;
        var row = checklistCard.Locator("table tbody tr").Filter(new() { HasText = taskTitleFragment }).First;
        var badge = row.Locator(".badge");
        return await badge.IsVisibleAsync() ? (await badge.TextContentAsync())?.Trim() : null;
    }


    private ILocator ProfilePhotoImage => page.Locator("img.hr-profile-avatar");
    private ILocator ProfilePhotoInitials => page.Locator("span.hr-profile-avatar--initials");

    private ILocator PendingProfilePhotoCard =>
        page.Locator(".alert-info").Filter(new() { HasText = "Pending Review" }).First;

    public async Task<bool> WaitForProfilePhotoImageAfterScanAsync()
    {
        try
        {
            await Assertions.Expect(ProfilePhotoImage).ToBeVisibleAsync(new() { Timeout = 120_000 });
            return true;
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    public async Task<bool> HasProfilePhotoImageAsync()
    {
        try
        {
            await Assertions.Expect(ProfilePhotoImage).ToBeVisibleAsync(new() { Timeout = 10_000 });
            return true;
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    public async Task<bool> HasProfilePhotoInitialsAsync()
    {
        try
        {
            await Assertions.Expect(ProfilePhotoInitials).ToBeVisibleAsync(new() { Timeout = 10_000 });
            return true;
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    public async Task UploadProfilePhotoDirectAsync(string filePath)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Upload / Replace Photo" }).ClickAsync();

        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Upload / Replace Profile Photo" });
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        await dialog.Locator("input[type='file']").SetInputFilesAsync(filePath);
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Upload", Exact = true }).ClickAsync();

        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }

    public async Task<bool> HasPendingProfilePhotoCardAsync()
    {
        try
        {
            await Assertions.Expect(PendingProfilePhotoCard).ToBeVisibleAsync(new() { Timeout = 10_000 });
            return true;
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    public async Task ApprovePendingProfilePhotoAsync()
    {
        var card = PendingProfilePhotoCard;
        await card.GetByRole(AriaRole.Button, new() { Name = "Approve" }).ClickAsync();
        await card.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }


    public async Task OpenNotesTabAsync()
    {
        await OpenSectionAsync("Notes");
        await page.WaitForSelectorAsync(
            "[data-testid='add-note-btn'], [data-testid='no-notes-message']",
            new() { Timeout = 15_000 });
    }

    public async Task<bool> HasNotesTabAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            try
            {
                if (await IsSectionTabPresentAsync(page, "Notes"))
                    return true;
            }
            catch (PlaywrightException)
            {
            }

            if (DateTime.UtcNow > deadline)
                return false;
            await page.WaitForTimeoutAsync(250);
        }
    }

    public async Task ClickAddNoteAsync()
    {
        var dialog = page.Locator("[role='dialog'].add-employee-note-dialog");
        var addNoteBtn = page.Locator("[data-testid='add-note-btn']");

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await addNoteBtn.ClickAsync();
            try
            {
                await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 3_000 });
                await WaitForCategoryComboboxAsync();
                return;
            }
            catch (TimeoutException)
            {
            }
        }

        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await WaitForCategoryComboboxAsync();

        async Task WaitForCategoryComboboxAsync() =>
            await dialog.Locator("span[role='combobox']").First.WaitForAsync(
                new() { State = WaitForSelectorState.Attached, Timeout = 10_000 });
    }

    public Task SelectAddNoteCategoryAsync(string categoryLabel) =>
        DropDownSelector.SelectAsync(page, page.Locator(".add-employee-note-dialog"), categoryLabel);

    public async Task FillAddNoteTextAsync(string text)
    {
        await page.GetByPlaceholder("Enter note details…").FillAsync(text);
        await page.Keyboard.PressAsync("Tab");
    }

    public Task CheckAddNoteImportantAsync() =>
        page.Locator(".add-employee-note-dialog").GetByLabel("Important").CheckAsync();

    public async Task SubmitAddNoteDialogAsync()
    {
        var dialog = page.Locator("[role='dialog'].add-employee-note-dialog");
        var addBtn = page.Locator(".add-employee-note-dialog .e-footer-content button:has-text('Add')");

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await addBtn.ClickAsync();
            try
            {
                await dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = attempt < 3 ? 4_000 : 10_000 });
                break;
            }
            catch (TimeoutException) when (attempt < 3)
            {
            }
        }

        await page.WaitForTimeoutAsync(250);
    }

    public Task<bool> HasAddNoteDialogErrorAsync() =>
        page.Locator(".add-employee-note-dialog .alert-danger").IsVisibleAsync();

    public Task<bool> IsNotesGridPagerVisibleAsync() =>
        page.Locator("[data-testid='employee-notes-grid'] .e-pagercontainer").IsVisibleAsync();

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

    public async Task FillSupersedeNoteTextAsync(string text)
    {
        await page.Locator("[data-testid='supersede-note-text']").FillAsync(text);
        await page.Keyboard.PressAsync("Tab");
    }

    public Task SelectSupersedeNoteCategoryAsync(string categoryLabel) =>
        DropDownSelector.SelectAsync(page, page.Locator(".supersede-employee-note-dialog"), categoryLabel);

    public async Task SubmitSupersedeNoteDialogAsync()
    {
        await page.Locator(".supersede-employee-note-dialog .e-footer-content button:has-text('Save')").ClickAsync();
        await page.Locator("[role='dialog'].supersede-employee-note-dialog").WaitForAsync(
            new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

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
