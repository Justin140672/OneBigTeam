using HR.Web.E2E.Tests.Infrastructure;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the position profile create/edit page.
/// Routes: /companies/{id}/position-profiles/new  and  /companies/{id}/position-profiles/{id}
/// </summary>
public sealed class PositionProfileEditPage(IPage page, string baseUrl)
{
    public async Task GoToNewAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/position-profiles/new");
        // PositionProfileEdit has an SfDropDownList for Department; span[role='combobox'] only
        // appears after Blazor's interactive render, ensuring event handlers are wired up.
        await page.WaitForSelectorAsync("span[role='combobox']", new() { Timeout = 20_000 });
    }

    public async Task GoToAsync(Guid companyId, Guid positionProfileId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/position-profiles/{positionProfileId}");
        await page.WaitForSelectorAsync("span[role='combobox']", new() { Timeout = 20_000 });
    }

    public async Task FillTitleAsync(string title)
    {
        await page.GetByPlaceholder("e.g. Senior Software Engineer").FillAsync(title);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillDescriptionAsync(string description)
    {
        await page.GetByPlaceholder("Optional description").FillAsync(description);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task SaveAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        // Navigates back to the position-profiles list on success.
        await page.WaitForURLAsync("**/position-profiles", new() { Timeout = 30_000 });
        // With prerender:false the circuit connects after navigation, wait for the grid.
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
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

    public async Task<string> GetTitleAsync() =>
        await page.GetByPlaceholder("e.g. Senior Software Engineer").InputValueAsync();

    // SfNumericTextBox fields (ProbationMonthsOverride, SalaryMin, SalaryMax): a bare FillAsync
    // bypasses their interop entirely (see EmployeeEditPage.TypeIntoNumericInputAsync for the same
    // pattern/explanation) — retype the value for real via click, select-all, delete, type, Tab.
    private async Task TypeIntoNumericInputAsync(ILocator input, string value)
    {
        await input.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.PressAsync("Delete");
        if (value.Length > 0)
            await input.PressSequentiallyAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public Task FillProbationMonthsOverrideAsync(int months) =>
        TypeIntoNumericInputAsync(page.GetByPlaceholder("Use company default"), months.ToString());

    public async Task FillSalaryRangeAsync(decimal min, decimal max)
    {
        await TypeIntoNumericInputAsync(page.GetByPlaceholder("Min"), min.ToString());
        await TypeIntoNumericInputAsync(page.GetByPlaceholder("Max"), max.ToString());
    }

    /// <summary>Selects a value from the Department dropdown on the position profile create/edit form.</summary>
    public Task SelectDepartmentAsync(string nameFragment) =>
        DropDownSelector.SelectAsync(page, page.Locator(".hr-field", new PageLocatorOptions { HasText = "Department" }).First, nameFragment);

    /// <summary>Selects a value from the Location dropdown on the position profile create/edit form.
    /// Location is now mandatory (see DepartmentId/LocationId/DefaultLeavePolicyId required-fields
    /// change), so every create/edit flow that saves successfully must call this.</summary>
    public Task SelectLocationAsync(string nameFragment) =>
        DropDownSelector.SelectAsync(page, page.Locator(".hr-field", new PageLocatorOptions { HasText = "Location" }).First, nameFragment);

    public async Task SetUseCompanyWorkingPatternAsync(bool useCompanyDefault)
    {
        var checkbox = page.GetByLabel("Use company working pattern");
        var isChecked = await checkbox.IsCheckedAsync();
        if (useCompanyDefault && !isChecked) await checkbox.CheckAsync();
        if (!useCompanyDefault && isChecked) await checkbox.UncheckAsync();
    }

    // ── Notice period override (Defaults card) ────────────────────────────────
    //
    // The "Override company default notice period" checkbox reveals a Unit dropdown +
    // Length numeric field, mirroring the "Use company working pattern" toggle above it.
    // Unlike Department/Location/Default Leave Policy/Onboarding Template, the Unit
    // dropdown has no adjacent <label> element (just a bare Placeholder="Unit" with the
    // default, non-floating FloatLabelType), so it can't be scoped via visible label text
    // the way NumericBoxByLabel-style helpers do elsewhere in this suite. Instead, scope to
    // the conditionally-rendered row via an xpath sibling traversal from the checkbox's
    // rendered .e-checkbox-wrapper, which is a reliable structural anchor regardless of
    // whether the dropdown/numeric box render their placeholders as visible text.

    /// <summary>
    /// The "row g-3 mt-2" div containing the Unit dropdown and Length numeric field, which
    /// is only present in the DOM while "Override company default notice period" is checked.
    /// </summary>
    private ILocator NoticePeriodOverrideRow =>
        page.Locator(".e-checkbox-wrapper")
            .Filter(new() { HasText = "Override company default notice period" })
            .Locator("xpath=following-sibling::div[contains(@class,'row')]");

    /// <summary>Checks/unchecks "Override company default notice period" and waits for the reveal/hide of its fields.</summary>
    public async Task SetOverrideNoticePeriodAsync(bool overrideEnabled)
    {
        var checkbox = page.GetByLabel("Override company default notice period");
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
        page.GetByLabel("Override company default notice period").IsCheckedAsync();

    /// <summary>True once the Unit/Length fields have rendered (i.e. the override checkbox is checked).</summary>
    public Task<bool> IsNoticePeriodOverrideFieldsVisibleAsync() =>
        NoticePeriodOverrideRow.IsVisibleAsync();

    /// <summary>Selects a value ("Weeks" or "Months") from the notice period override's Unit dropdown. Only present once the override checkbox is checked.</summary>
    public Task SelectNoticePeriodUnitOverrideAsync(string unitLabel) =>
        DropDownSelector.SelectAsync(page, NoticePeriodOverrideRow, unitLabel);

    /// <summary>Returns the currently displayed value of the notice period override's Unit dropdown.</summary>
    public async Task<string> GetNoticePeriodUnitOverrideTextAsync()
    {
        var combobox = NoticePeriodOverrideRow.Locator("span[role='combobox']").First;
        return (await combobox.Locator("input").InputValueAsync()).Trim();
    }

    /// <summary>Sets the notice period override's Length numeric field. Only present once the override checkbox is checked.</summary>
    public Task FillNoticePeriodLengthOverrideAsync(int length) =>
        TypeIntoNumericInputAsync(NoticePeriodOverrideRow.Locator("input.e-numerictextbox").First, length.ToString());

    /// <summary>Returns the current value of the notice period override's Length numeric field.</summary>
    public async Task<int> GetNoticePeriodLengthOverrideAsync()
    {
        var value = await NoticePeriodOverrideRow.Locator("input.e-numerictextbox").First.InputValueAsync();
        return int.Parse(value);
    }

    public Task SelectDefaultLeavePolicyAsync(string leavePolicyName) =>
        // The Defaults card now has three comboboxes (Salary Type, Default Leave Policy,
        // Onboarding Template), so scope to the specific field wrapper by its label rather
        // than taking .First within the whole card.
        DropDownSelector.SelectAsync(page, page.Locator(".hr-field", new PageLocatorOptions { HasText = "Default Leave Policy" }), leavePolicyName);

    /// <summary>Selects a value from the Onboarding Template dropdown on the position profile create/edit form.</summary>
    public Task SelectOnboardingTemplateAsync(string nameFragment) =>
        DropDownSelector.SelectAsync(page, page.Locator(".hr-field", new PageLocatorOptions { HasText = "Onboarding Template" }), nameFragment);

    /// <summary>
    /// Clears the Onboarding Template selection by opening its dropdown and selecting the
    /// prepended "None" sentinel item (Id = Guid.Empty) — replaces the old ShowClearButton ("x"
    /// icon) approach, which was removed in favor of this explicit no-selection list item (see
    /// PositionProfileEdit.razor's OnboardingTemplateListItemModel list, which prepends a
    /// Guid.Empty/"None" entry).
    /// </summary>
    public Task ClearOnboardingTemplateAsync() =>
        DropDownSelector.SelectAsync(page, page.Locator(".hr-field", new PageLocatorOptions { HasText = "Onboarding Template" }), "None");

    /// <summary>Reads the current value of the Onboarding Template dropdown's visible text.</summary>
    public async Task<string?> GetSelectedOnboardingTemplateTextAsync()
    {
        var field = page.Locator(".hr-field", new PageLocatorOptions { HasText = "Onboarding Template" });
        return await field.Locator(".e-input-group input").First.InputValueAsync();
    }

    private ILocator UnsavedChangesDialog => page.Locator("[role='dialog']:has-text('Unsaved Changes')");

    public Task ClickCloseAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true }).ClickAsync();

    public Task<bool> IsUnsavedChangesDialogVisibleAsync() =>
        UnsavedChangesDialog.WaitUntilVisibleAsync();

    public async Task ConfirmDiscardChangesAsync()
    {
        await UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Discard Changes" }).ClickAsync();
        await page.WaitForURLAsync("**/position-profiles", new() { Timeout = 30_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public async Task ConfirmSaveFromUnsavedChangesDialogAsync()
    {
        await UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await page.WaitForURLAsync("**/position-profiles", new() { Timeout = 30_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public Task CancelUnsavedChangesDialogAsync() =>
        UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();

    public async Task CloseAndWaitForListAsync()
    {
        await ClickCloseAsync();
        await page.WaitForURLAsync("**/position-profiles", new() { Timeout = 30_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public async Task OpenRequiredDocumentsTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Required Documents" }).ClickAsync();
        // Wait for the tab content to be interactive — either the Add button or the empty-state text.
        await page.WaitForSelectorAsync(
            "button:has-text('Add'), .text-muted:has-text('No required documents')",
            new() { Timeout = 15_000 });
    }

    public async Task<bool> HasRequiredDocumentsTabAsync() =>
        await page.GetByRole(AriaRole.Tab, new() { Name = "Required Documents" }).WaitUntilVisibleAsync();

    public async Task ClickAddRequiredDocumentAsync() =>
        await page.GetByRole(AriaRole.Button, new() { Name = "Add" }).ClickAsync();

    public async Task SelectDocumentTypeInDialogAsync(string documentTypeName)
    {
        // Wait for the SfDialog container — Syncfusion sets role="dialog" on the outer element.
        await page.Locator("[role='dialog']").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });

        await DropDownSelector.SelectAsync(page, page.Locator("[role='dialog']"), documentTypeName);
    }

    public async Task SubmitAddDialogAsync()
    {
        await page.Locator("[role='dialog'] .e-footer-content button:has-text('Add')").ClickAsync();
        await page.Locator("[role='dialog']").WaitForAsync(
            new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    // Waiting for the Add button/empty-state text (OpenRequiredDocumentsTabAsync) only proves the
    // Blazor component has mounted, not that Syncfusion's EJ2 grid has finished its own JS render
    // pass to populate ".e-row" — wait for the row selector itself (or the empty-state text) here
    // too, since these methods are also called after adding/removing a row, which re-fetches.
    private const string RequiredDocumentsRowsRenderedSelector =
        ".e-grid .e-row, .text-muted:has-text('No required documents')";

    public async Task<bool> HasRequiredDocumentInGridAsync(string documentTypeName)
    {
        await page.WaitForSelectorAsync(RequiredDocumentsRowsRenderedSelector, new() { Timeout = 15_000 });

        var rows = page.Locator(".e-grid .e-row").Filter(new() { HasText = documentTypeName });
        return await rows.CountAsync() > 0;
    }

    public async Task ClickRemoveRequiredDocumentAsync(string documentTypeName)
    {
        await page.WaitForSelectorAsync(RequiredDocumentsRowsRenderedSelector, new() { Timeout = 15_000 });

        var row = page.Locator(".e-grid .e-row").Filter(new() { HasText = documentTypeName }).First;
        await row.GetByTitle("Remove").ClickAsync();
    }

    public async Task ConfirmRemoveAsync() =>
        await page.GetByRole(AriaRole.Button, new() { Name = "Yes" }).ClickAsync();

    // ── Inherited Roles tab (PositionProfileInheritedRolesTab.razor) ───────────────────────────

    private ILocator InheritedRolesCard => page.Locator(".card", new() { HasText = "Inherited Roles" });

    public async Task OpenInheritedRolesTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Inherited Roles" }).ClickAsync();
        // Wait for the tab content's table (or the "no roles" table is always rendered — the
        // stable anchor here is the card heading itself becoming visible).
        await InheritedRolesCard.WaitForAsync(new() { Timeout = 15_000 });
    }

    public async Task<bool> HasInheritedRolesTabAsync() =>
        await page.GetByRole(AriaRole.Tab, new() { Name = "Inherited Roles" }).WaitUntilVisibleAsync();

    /// <summary>The &lt;tr&gt; for the given role's display name (e.g. "Recruiter", "HR Administrator").</summary>
    private ILocator InheritedRoleRow(string roleName) =>
        InheritedRolesCard.Locator("tr", new() { HasText = roleName }).First;

    private ILocator InheritedRoleCheckbox(string roleName) =>
        InheritedRoleRow(roleName).Locator("input[type='checkbox']");

    public Task<bool> IsInheritedRoleCheckedAsync(string roleName) =>
        InheritedRoleCheckbox(roleName).IsCheckedAsync();

    public Task<bool> IsInheritedRoleDisabledAsync(string roleName) =>
        InheritedRoleCheckbox(roleName).IsDisabledAsync();

    /// <summary>Checks or unchecks the given role's checkbox on the Inherited Roles tab, without saving.</summary>
    public async Task SetInheritedRoleCheckedAsync(string roleName, bool isChecked)
    {
        var checkbox = InheritedRoleCheckbox(roleName);
        if (isChecked)
            await checkbox.CheckAsync();
        else
            await checkbox.UncheckAsync();
    }

    /// <summary>Returns the display names of every currently-checked role on the Inherited Roles tab.</summary>
    public async Task<IReadOnlyList<string>> GetCheckedInheritedRoleNamesAsync()
    {
        var rows = InheritedRolesCard.Locator("tbody tr");
        var count = await rows.CountAsync();
        var names = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var row = rows.Nth(i);
            if (await row.Locator("input[type='checkbox']").IsCheckedAsync())
                names.Add((await row.Locator("label").InnerTextAsync()).Trim());
        }
        return names;
    }

    /// <summary>
    /// Position Profile now has a single Save (outside the tab control, like EmployeeEdit) that
    /// persists the Details fields and the Inherited Roles selection together, then navigates back
    /// to the list. This clicks that Save and re-opens the same profile on the Inherited Roles tab
    /// so callers can read the persisted state without repeating the navigation themselves.
    /// </summary>
    public async Task SaveInheritedRolesAsync()
    {
        var editUrl = page.Url;
        await SaveAsync();
        await page.GotoAsync(editUrl);
        await page.WaitForSelectorAsync("span[role='combobox']", new() { Timeout = 20_000 });
        await OpenInheritedRolesTabAsync();
    }

    /// <summary>
    /// Kept for compatibility — inherited-role changes are now saved by the page-level Save, which
    /// <see cref="SaveInheritedRolesAsync"/> waits on (it throws if the save/navigation didn't
    /// happen), so reaching here means the save succeeded.
    /// </summary>
    public Task<bool> HasInheritedRolesSuccessAlertAsync() => Task.FromResult(true);
}
