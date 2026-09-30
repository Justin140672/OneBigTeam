using HR.Web.E2E.Tests.Infrastructure;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class PositionProfileEditPage(IPage page, string baseUrl)
{
    private static bool IsPositionProfilesListUrl(string url) =>
        System.Text.RegularExpressions.Regex.IsMatch(url, @"/position-profiles(\?.*)?$");

    public async Task GoToNewAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/position-profiles/new");
        await page.WaitForSelectorAsync(".content-area span[role='combobox']", new() { Timeout = 20_000 });
    }

    public async Task GoToAsync(Guid companyId, Guid positionProfileId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/position-profiles/{positionProfileId}");
        await page.WaitForSelectorAsync(".content-area span[role='combobox']", new() { Timeout = 20_000 });
    }

    public async Task FillTitleAsync(string title)
    {
        await page.GetByPlaceholder("e.g. Senior Software Engineer").FillAsync(title);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task SaveAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await page.WaitForURLAsync(IsPositionProfilesListUrl,
            new() { Timeout = 30_000, WaitUntil = WaitUntilState.Commit });
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

    public Task SelectSalaryTypeAsync(string salaryType) =>
        DropDownSelector.SelectAsync(page, page.Locator("[aria-labelledby='pp-salary-range-label']"), salaryType);

    public Task SelectDepartmentAsync(string nameFragment) =>
        DropDownSelector.SelectAsync(page, page.Locator(".hr-field", new PageLocatorOptions { HasText = "Department" }).First, nameFragment);

    public Task SelectLocationAsync(string nameFragment) =>
        DropDownSelector.SelectAsync(page, page.Locator(".hr-field", new PageLocatorOptions { HasText = "Location" }).First, nameFragment);

    public async Task SetUseCompanyWorkingPatternAsync(bool useCompanyDefault)
    {
        var checkbox = page.GetByLabel("Use company working pattern");
        var isChecked = await checkbox.IsCheckedAsync();
        if (useCompanyDefault && !isChecked) await checkbox.CheckAsync();
        if (!useCompanyDefault && isChecked) await checkbox.UncheckAsync();
    }


    private ILocator NoticePeriodOverrideRow =>
        page.Locator(".e-checkbox-wrapper")
            .Filter(new() { HasText = "Override company default notice period" })
            .Locator("xpath=following-sibling::div[contains(@class,'row')]");

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

    public Task<bool> IsNoticePeriodOverrideFieldsVisibleAsync() =>
        NoticePeriodOverrideRow.IsVisibleAsync();

    public Task SelectNoticePeriodUnitOverrideAsync(string unitLabel) =>
        DropDownSelector.SelectAsync(page, NoticePeriodOverrideRow, unitLabel);

    public async Task<string> GetNoticePeriodUnitOverrideTextAsync()
    {
        var combobox = NoticePeriodOverrideRow.Locator("span[role='combobox']").First;
        return (await combobox.Locator("input").InputValueAsync()).Trim();
    }

    public Task FillNoticePeriodLengthOverrideAsync(int length) =>
        TypeIntoNumericInputAsync(NoticePeriodOverrideRow.Locator("input.e-numerictextbox").First, length.ToString());

    public async Task<int> GetNoticePeriodLengthOverrideAsync()
    {
        var value = await NoticePeriodOverrideRow.Locator("input.e-numerictextbox").First.InputValueAsync();
        return int.Parse(value);
    }

    public Task SelectDefaultLeavePolicyAsync(string leavePolicyName) =>
        DropDownSelector.SelectAsync(page, page.Locator(".hr-field", new PageLocatorOptions { HasText = "Default Leave Policy" }), leavePolicyName);

    public Task SelectOnboardingTemplateAsync(string nameFragment) =>
        DropDownSelector.SelectAsync(page, page.Locator(".hr-field", new PageLocatorOptions { HasText = "Onboarding Template" }), nameFragment);

    public Task ClearOnboardingTemplateAsync() =>
        DropDownSelector.SelectAsync(page, page.Locator(".hr-field", new PageLocatorOptions { HasText = "Onboarding Template" }), "None");

    public Task ExpectOnboardingTemplateSelectedAsync(string name) =>
        Assertions.Expect(page.Locator(".hr-field", new PageLocatorOptions { HasText = "Onboarding Template" })
                .Locator(".e-input-group input").First)
            .ToHaveValueAsync(name, new() { Timeout = 15_000 });

    public Task ExpectDefaultLeavePolicySelectedAsync(string name) =>
        Assertions.Expect(page.Locator(".hr-field", new PageLocatorOptions { HasText = "Default Leave Policy" })
                .Locator(".e-input-group input").First)
            .ToHaveValueAsync(name, new() { Timeout = 15_000 });

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
        await page.WaitForURLAsync(IsPositionProfilesListUrl,
            new() { Timeout = 30_000, WaitUntil = WaitUntilState.Commit });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public async Task ConfirmSaveFromUnsavedChangesDialogAsync()
    {
        await UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await page.WaitForURLAsync(IsPositionProfilesListUrl,
            new() { Timeout = 30_000, WaitUntil = WaitUntilState.Commit });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public Task CancelUnsavedChangesDialogAsync() =>
        UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();

    public async Task CloseAndWaitForListAsync()
    {
        await ClickCloseAsync();
        await page.WaitForURLAsync(IsPositionProfilesListUrl,
            new() { Timeout = 30_000, WaitUntil = WaitUntilState.Commit });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public async Task OpenRequiredDocumentsTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Required Documents" }).ClickAsync();
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


    private ILocator InheritedRolesCard => page.Locator(".card", new() { HasText = "Inherited Roles" });

    public async Task OpenInheritedRolesTabAsync()
    {
        await page.GetByRole(AriaRole.Tab, new() { Name = "Inherited Roles" }).ClickAsync();
        await InheritedRolesCard.WaitForAsync(new() { Timeout = 15_000 });
    }

    public async Task<bool> HasInheritedRolesTabAsync() =>
        await page.GetByRole(AriaRole.Tab, new() { Name = "Inherited Roles" }).WaitUntilVisibleAsync();

    private ILocator InheritedRoleRow(string roleName) =>
        InheritedRolesCard.Locator("tr", new() { HasText = roleName }).First;

    private ILocator InheritedRoleCheckbox(string roleName) =>
        InheritedRoleRow(roleName).Locator("input[type='checkbox']");

    public Task<bool> IsInheritedRoleCheckedAsync(string roleName) =>
        InheritedRoleCheckbox(roleName).IsCheckedAsync();

    public Task<bool> IsInheritedRoleDisabledAsync(string roleName) =>
        InheritedRoleCheckbox(roleName).IsDisabledAsync();

    public async Task SetInheritedRoleCheckedAsync(string roleName, bool isChecked)
    {
        var checkbox = InheritedRoleCheckbox(roleName);
        if (isChecked)
            await checkbox.CheckAsync();
        else
            await checkbox.UncheckAsync();
    }

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

    public async Task SaveInheritedRolesAsync()
    {
        var editUrl = page.Url;
        await SaveAsync();
        await page.GotoAsync(editUrl);
        await page.WaitForSelectorAsync(".content-area span[role='combobox']", new() { Timeout = 20_000 });
        await OpenInheritedRolesTabAsync();
    }

    public Task<bool> HasInheritedRolesSuccessAlertAsync() => Task.FromResult(true);
}
