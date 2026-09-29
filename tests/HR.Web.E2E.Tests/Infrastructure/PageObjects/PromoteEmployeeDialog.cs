using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class PromoteEmployeeDialog(IPage page)
{
    private ILocator Dialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Promote Employee" });
    private ILocator UnsavedChangesDialog => page.Locator("[role='dialog']:has-text('Unsaved Changes')");

    public async Task OpenAsync()
    {
        await page.Locator("[data-testid='promote-employee-btn']").ClickAsync();
        await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    public Task<bool> IsVisibleAsync() => Dialog.IsVisibleAsync();

    public async Task<string?> GetActiveStepLabelAsync()
    {
        var active = Dialog.Locator(".hr-stepper-item--current");
        var index = (await active.Locator(".hr-stepper-node").TextContentAsync())?.Trim();
        var label = (await active.Locator(".hr-stepper-label").TextContentAsync())?.Trim();
        return $"{index}. {label}";
    }


    public async Task<string?> GetCurrentPositionTextAsync()
    {
        var input = Dialog.Locator(".col-12").Filter(new() { HasText = "Current Position" })
            .First.Locator("input");
        return (await input.InputValueAsync())?.Trim();
    }

    public Task SelectNewPositionProfileAsync(string profileTitle) =>
        DropDownSelector.SelectAsync(page, Dialog.Locator(".col-12").Filter(new() { HasText = "New Position Profile" }).First, profileTitle);

    public async Task<IReadOnlyList<string>> GetNewPositionProfileDropdownOptionsAsync()
    {
        var field = Dialog.Locator(".col-12").Filter(new() { HasText = "New Position Profile" }).First;
        await field.Locator("span[role='combobox']").First.ClickAsync();
        await page.WaitForSelectorAsync(".e-popup.e-ddl:visible", new() { Timeout = 10_000 });

        var items = await page.Locator(".e-popup.e-ddl:visible .e-list-item").AllAsync();
        var titles = new List<string>();
        foreach (var item in items)
            titles.Add((await item.TextContentAsync())?.Trim() ?? "");

        await page.Keyboard.PressAsync("Escape");
        return titles;
    }

    public async Task FillEffectiveDateAsync(string ddMMyyyy)
    {
        var input = Dialog.Locator(".e-date-wrapper input.e-input").First;
        await input.ClickAsync();
        await input.FillAsync(ddMMyyyy);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillReasonAsync(string reason)
    {
        await Dialog.GetByPlaceholder("e.g. Annual review promotion").FillAsync(reason);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillNotesAsync(string notes)
    {
        await Dialog.GetByPlaceholder("Optional notes…").FillAsync(notes);
        await page.Keyboard.PressAsync("Tab");
    }


    public Task CheckChangeManagerAsync() => Dialog.GetByLabel("Change manager").CheckAsync();

    public Task CheckChangeLocationAsync() => Dialog.GetByLabel("Change location").CheckAsync();

    public Task SelectNewManagerAsync(string managerNameFragment) =>
        DropDownSelector.SelectAsync(page, Dialog.Locator(".col-12").Filter(new() { HasText = "New Manager" }).First, managerNameFragment);

    public Task SelectNewLocationAsync(string locationNameFragment) =>
        DropDownSelector.SelectAsync(page, Dialog.Locator(".col-12").Filter(new() { HasText = "New Location" }).First, locationNameFragment);


    public async Task CheckCreateCompensationChangeAsync()
    {
        await Dialog.GetByLabel("Create compensation change").CheckAsync();
        await Dialog.GetByText("Salary Type").WaitForAsync(new() { Timeout = 5_000 });
    }

    public Task SelectCompensationSalaryTypeAsync(string salaryType) =>
        DropDownSelector.SelectAsync(page, Dialog.Locator(".col-6").Filter(new() { HasText = "Salary Type" }).First, salaryType);

    // Salary/Hours Per Week/FTE are all SfNumericTextBox instances without an explicit
    // FloatLabelType override, so Syncfusion's default (Never) should render Placeholder as a
    // real HTML placeholder attribute — but to avoid relying on that assumption (see
    // EmployeeEditPage's own compensation-dialog fields, which use FloatLabelType.Auto and so
    // can't be targeted by placeholder at all), scope by column position instead: Salary,
    // Hours Per Week and FTE are the only three ".e-numerictextbox" inputs on this step, always
    // rendered in that order (Salary Type is a dropdown, Currency is a plain text box).
    // SfNumericTextBox: a bare FillAsync bypasses its interop entirely (see EmployeeEditPage.
    // TypeIntoNumericInputAsync for the same pattern/explanation) — retype the value for real.
    private async Task TypeIntoNumericInputAsync(ILocator input, string value)
    {
        await input.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.PressAsync("Delete");
        if (value.Length > 0)
            await input.PressSequentiallyAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public Task FillCompensationSalaryAsync(string value) =>
        TypeIntoNumericInputAsync(Dialog.Locator("input.e-numerictextbox").Nth(0), value);

    public async Task FillCompensationCurrencyAsync(string value)
    {
        await Dialog.GetByPlaceholder("e.g. GBP").FillAsync(value);
        await page.Keyboard.PressAsync("Tab");
    }

    public Task FillCompensationHoursPerWeekAsync(string value) =>
        TypeIntoNumericInputAsync(Dialog.Locator("input.e-numerictextbox").Nth(1), value);

    public Task FillCompensationFteAsync(string value) =>
        TypeIntoNumericInputAsync(Dialog.Locator("input.e-numerictextbox").Nth(2), value);


    public async Task<string?> GetConfirmationValueAsync(string label)
    {
        var dt = Dialog.Locator("dl.row dt").Filter(new() { HasText = label }).First;
        if (!await dt.IsVisibleAsync())
            return null;
        var dd = dt.Locator("xpath=following-sibling::dd[1]");
        return (await dd.TextContentAsync())?.Trim();
    }


    public async Task ClickNextAsync()
    {
        var activeStepLocator = Dialog.Locator(".hr-stepper-item--current");
        var beforeLabel = (await activeStepLocator.TextContentAsync())?.Trim() ?? string.Empty;

        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Next" }).ClickAsync();

        try
        {
            await Assertions.Expect(activeStepLocator).Not.ToHaveTextAsync(beforeLabel, new() { Timeout = 8_000 });
        }
        catch (PlaywrightException)
        {
            await Dialog.Locator(".alert-danger")
                .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 8_000 });
        }
    }

    public Task ClickBackAsync() =>
        Dialog.GetByRole(AriaRole.Button, new() { Name = "Back" }).ClickAsync();

    public async Task SubmitAsync()
    {
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Promote" }).ClickAsync();

        try
        {
            await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
            await WaitForOverlayToClearAsync();
        }
        catch (TimeoutException)
        {
            await Dialog.Locator(".alert-danger")
                .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 8_000 });

            var confirmButton = Dialog.GetByRole(AriaRole.Button, new() { Name = "Confirm & Promote" });
            if (await confirmButton.IsVisibleAsync())
            {
                await confirmButton.ClickAsync();
                await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
                await WaitForOverlayToClearAsync();
            }
        }
    }

    private async Task WaitForOverlayToClearAsync()
    {
        try
        {
            await page.Locator(".e-dlg-overlay").WaitForAsync(
                new() { State = WaitForSelectorState.Detached, Timeout = 5_000 });
        }
        catch (TimeoutException)
        {
        }
    }

    public async Task CancelAsync()
    {
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();    }

    public async Task ConfirmDiscardChangesAsync()
    {
        await UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Discard Changes" }).ClickAsync();
        await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    public async Task<string?> GetGlobalErrorAsync()
    {
        var error = Dialog.Locator(".alert-danger").First;
        return await error.IsVisibleAsync() ? (await error.TextContentAsync())?.Trim() : null;
    }
}
