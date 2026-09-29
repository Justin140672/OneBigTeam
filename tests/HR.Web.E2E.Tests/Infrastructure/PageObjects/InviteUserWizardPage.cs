using HR.Web.E2E.Tests.Infrastructure;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class InviteUserWizardPage(IPage page)
{
    private ILocator Dialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Invite User" });

    public async Task OpenFromToolbarAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Invite User" }).First.ClickAsync();
        await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    public async Task SelectEmployeeAsync(string employeeName)
    {
        await DropDownSelector.SelectAsync(page, Dialog, employeeName);
        await ClickNextAsync();
    }

    public async Task ConfirmEmailAsync(string? email = null)
    {
        if (email is not null)
            await Dialog.Locator("input[type='text']").First.FillAsync(email);
        await ClickNextAsync();
    }

    public async Task SelectRolesAsync(params string[] roleNames)
    {
        foreach (var roleName in roleNames)
        {
            await Dialog.Locator("tr", new() { HasText = roleName })
                .Locator("input[type='checkbox']")
                .First
                .CheckAsync();
        }
        await ClickNextAsync();
    }

    public async Task SendAsync()
    {
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Send invitation" }).ClickAsync();
        await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 20_000 });
    }

    public async Task CancelAsync()
    {
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
        await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    public Task<string> GetReviewTextAsync() => Dialog.InnerTextAsync();

    private async Task ClickNextAsync()
    {
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Next" }).ClickAsync();
        await page.WaitForTimeoutAsync(250);
    }


    public Task<bool> IsOpenAsync() => Dialog.IsVisibleAsync();

    public async Task<string> GetActiveStepLabelAsync() =>
        (await Dialog.Locator(".hr-stepper-item--current .hr-stepper-label").First.InnerTextAsync()).Trim();

    public async Task<string?> GetFieldValidationMessageAsync()
    {
        var msg = Dialog.Locator(".validation-message").First;
        return await msg.IsVisibleAsync() ? (await msg.InnerTextAsync()).Trim() : null;
    }

    public Task SelectEmployeeWithoutAdvancingAsync(string employeeName) =>
        DropDownSelector.SelectAsync(page, Dialog, employeeName);

    public async Task FillEmailFieldAsync(string email)
    {
        var input = Dialog.Locator("input[type='text']").First;
        await input.FillAsync(email);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task ClickNextExpectingNoAdvanceAsync()
    {
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Next" }).ClickAsync();
        try
        {
            await Dialog.Locator(".validation-message").First
                .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 8_000 });
        }
        catch (TimeoutException)
        {
        }
    }

    public async Task ClickNextExpectingAdvanceAsync(string expectedStepLabel)
    {
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Next" }).ClickAsync();
        await Assertions.Expect(Dialog.Locator(".hr-stepper-item--current .hr-stepper-label"))
            .ToHaveTextAsync(expectedStepLabel, new() { Timeout = 8_000 });
    }
}
