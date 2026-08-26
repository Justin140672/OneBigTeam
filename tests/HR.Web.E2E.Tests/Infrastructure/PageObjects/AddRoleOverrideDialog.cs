using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for the "Add Permission Override" dialog (AddRoleOverrideDialog.razor), opened
/// from UserDetail.razor's "+ Add Override" button (only visible when the current user has
/// manage permission — same gate as "Manage Roles"). Fields: Role (SfDropDownList, excludes
/// roles that already have an active override for this user), Override Type
/// (Grant/Deny SfDropDownList), Reason (required HrTextBox), Expires (optional SfDatePicker).
/// </summary>
public sealed class AddRoleOverrideDialog(IPage page)
{
    private ILocator Dialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Add Permission Override" });

    public Task<bool> IsVisibleAsync() => Dialog.IsVisibleAsync();

    /// <summary>
    /// Selects a role from the "Role" dropdown. Only roles without an existing active override
    /// for this user are offered (AddRoleOverrideDialog.razor's ExistingOverrideRoleIds filter).
    /// </summary>
    public Task SelectRoleAsync(string roleName) =>
        DropDownSelector.SelectAsync(page, Dialog, roleName, index: 0);

    /// <summary>Selects "Grant" or "Deny" from the Override Type dropdown.</summary>
    public Task SelectOverrideTypeAsync(string grantOrDeny) =>
        DropDownSelector.SelectAsync(page, Dialog, grantOrDeny, index: 1);

    public async Task FillReasonAsync(string reason)
    {
        await Dialog.GetByPlaceholder("Why is this override needed?").FillAsync(reason);
        await page.Keyboard.PressAsync("Tab");
    }

    public async Task FillExpiresAsync(string ddMMyyyy)
    {
        var input = Dialog.Locator(".e-date-wrapper input.e-input").First;
        await input.ClickAsync();
        await input.FillAsync(ddMMyyyy);
        await page.Keyboard.PressAsync("Tab");
    }

    /// <summary>
    /// Clicks "Save". Does not assume success — on validation/server failure the dialog stays
    /// open with an inline ".alert-danger.py-2" instead (see <see cref="GetErrorAsync"/>).
    /// </summary>
    public async Task SaveAsync()
    {
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();

        try
        {
            await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
        }
        catch (TimeoutException)
        {
            await Dialog.Locator(".alert-danger")
                .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 8_000 });
        }
    }

    /// <summary>Dismisses the dialog by clicking "Cancel".</summary>
    public async Task CancelAsync()
    {
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
        await Dialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    /// <summary>
    /// Returns the inline client/server error text (AddRoleOverrideDialog's own `_error`, e.g.
    /// "Select a role.", "Select Grant or Deny.", "A reason is required.", or a server-side
    /// failure message), or null if none is currently visible.
    /// </summary>
    public async Task<string?> GetErrorAsync()
    {
        var error = Dialog.Locator(".alert-danger").First;
        return await error.IsVisibleAsync() ? (await error.TextContentAsync())?.Trim() : null;
    }
}
