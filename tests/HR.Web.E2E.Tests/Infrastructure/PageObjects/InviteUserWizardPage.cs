using HR.Web.E2E.Tests.Infrastructure;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Drives the 4-step "Invite User" wizard (InviteUserWizard.razor) opened from the User
/// Administration list page's "Invite User" toolbar action.
///
/// RISK: InviteUserWizard.razor currently exposes no data-testid / stable ids on its step
/// controls, so this page object locates by the SfDialog's accessible name ("Invite User") plus
/// role/label/button text, and drives the employee SfDropDownList through the shared
/// <see cref="DropDownSelector"/>. If the wizard markup changes (step labels, button captions),
/// these locators need revisiting — adding data-testids to the wizard would make this robust.
/// </summary>
public sealed class InviteUserWizardPage(IPage page)
{
    private ILocator Dialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Invite User" });

    public async Task OpenFromToolbarAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Invite User" }).First.ClickAsync();
        await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    /// <summary>Step 1 — pick the employee via the Syncfusion combobox (shared helper, per convention).</summary>
    public async Task SelectEmployeeAsync(string employeeName)
    {
        await DropDownSelector.SelectAsync(page, Dialog, employeeName);
        await ClickNextAsync();
    }

    /// <summary>Step 2 — confirm / enter the work email.</summary>
    public async Task ConfirmEmailAsync(string? email = null)
    {
        if (email is not null)
            await Dialog.Locator("input[type='text']").First.FillAsync(email);
        await ClickNextAsync();
    }

    /// <summary>Step 3 — tick any additional roles (Employee is always applied automatically).</summary>
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

    /// <summary>Step 4 — review, then send.</summary>
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
}
