using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class EmploymentTypeEditPage(IPage page, string baseUrl)
{
    public async Task GoToNewAsync(Guid companyId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/employment-types/new");
        await page.WaitForSelectorAsync("button:has-text('Save')", new() { Timeout = 20_000 });
    }

    public async Task GoToAsync(Guid companyId, Guid employmentTypeId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/employment-types/{employmentTypeId}");
        await page.WaitForSelectorAsync("button:has-text('Save')", new() { Timeout = 20_000 });
    }

    public Task FillNameAsync(string name) =>
        FillTextBoxAsync("e.g. Permanent, Contractor", name);

    public Task FillDescriptionAsync(string description) =>
        FillTextBoxAsync("Optional description", description);

    // This page is @rendermode InteractiveServer: straight after ClickNewAsync the SignalR circuit
    // may not have wired up the SfTextBox's oninput handler yet, so a one-shot FillAsync sets the
    // DOM value but Blazor never binds it — Save then posts an empty Name, validation blocks the
    // navigation, and the caller's WaitForURL times out. Wait for the field, type character by
    // character (each keystroke raises its own input event once the circuit is live), then verify
    // the value actually committed and retype once if it didn't.
    private async Task FillTextBoxAsync(string placeholder, string value)
    {
        var input = page.GetByPlaceholder(placeholder);
        await input.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 20_000 });

        for (var attempt = 0; attempt < 2; attempt++)
        {
            await input.ClickAsync();
            await page.Keyboard.PressAsync("Control+A");
            await page.Keyboard.PressAsync("Delete");
            await input.PressSequentiallyAsync(value, new() { Delay = 20 });
            await page.Keyboard.PressAsync("Tab");

            if (await input.InputValueAsync() == value)
                return;

            await page.WaitForTimeoutAsync(250);
        }
    }

    public async Task SaveAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await page.WaitForURLAsync("**/employment-types", new() { Timeout = 30_000 });
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

    public async Task<string> GetNameAsync() =>
        await page.GetByPlaceholder("e.g. Permanent, Contractor").InputValueAsync();

    private ILocator UnsavedChangesDialog => page.Locator("[role='dialog']:has-text('Unsaved Changes')");

    public Task ClickCloseAsync() =>
        page.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true }).ClickAsync();

    public Task<bool> IsUnsavedChangesDialogVisibleAsync() =>
        UnsavedChangesDialog.WaitUntilVisibleAsync();

    public async Task ConfirmDiscardChangesAsync()
    {
        await UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Discard Changes" }).ClickAsync();
        await page.WaitForURLAsync("**/employment-types", new() { Timeout = 30_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public async Task ConfirmSaveFromUnsavedChangesDialogAsync()
    {
        await UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await page.WaitForURLAsync("**/employment-types", new() { Timeout = 30_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }

    public Task CancelUnsavedChangesDialogAsync() =>
        UnsavedChangesDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();

    public async Task CloseAndWaitForListAsync()
    {
        await ClickCloseAsync();
        await page.WaitForURLAsync("**/employment-types", new() { Timeout = 30_000 });
        await page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });
    }
}
