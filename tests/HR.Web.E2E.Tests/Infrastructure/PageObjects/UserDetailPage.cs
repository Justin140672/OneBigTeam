using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Interacts with UserDetail.razor ("/companies/{CompanyId}/user-administration/{EmployeeId}")
/// and the ManageUserRolesDialog.razor it opens via "Manage Roles".
/// </summary>
public sealed class UserDetailPage(IPage page, string baseUrl)
{
    private const string LoadedSelector = ".card, .alert-warning";

    public async Task GoToAsync(Guid companyId, Guid employeeId)
    {
        await page.GotoAsync($"{baseUrl}/companies/{companyId}/user-administration/{employeeId}");
        await page.WaitForSelectorAsync(LoadedSelector, new() { Timeout = 20_000 });
    }

    public async Task<string> GetAccountStatusAsync()
    {
        await page.WaitForSelectorAsync(LoadedSelector, new() { Timeout = 15_000 });
        return await page.Locator(".d-flex.align-items-center.gap-2.mb-1 span.badge").First.InnerTextAsync();
    }

    public async Task<int> GetAuditHistoryCountAsync()
    {
        await page.WaitForSelectorAsync(LoadedSelector, new() { Timeout = 15_000 });
        var card = page.Locator(".card", new() { HasText = "Audit History" });
        return await card.Locator("ul.list-unstyled > li").CountAsync();
    }

    public async Task<bool> HasAuditHistoryEmptyMessageAsync()
    {
        var card = page.Locator(".card", new() { HasText = "Audit History" });
        return await card.GetByText("No audit history recorded for this user.").IsVisibleAsync();
    }

    public async Task ResendInvitationAsync() => await ClickActionAsync("Resend Invitation");

    public async Task CancelInvitationAsync() => await ClickActionAsync("Cancel Invitation");

    public async Task DisableAccountAsync() => await ClickActionAsync("Disable Account");

    public async Task EnableAccountAsync() => await ClickActionAsync("Enable Account");

    private async Task ClickActionAsync(string buttonName)
    {
        var button = page.GetByRole(AriaRole.Button, new() { Name = buttonName });
        await button.ClickAsync();

        // UserDetail.razor renders no spinner during _actionInProgress (Disabled="@_actionInProgress"
        // is the only visual cue), so there is nothing to wait on there. Instead wait for the clicked
        // button itself to disappear — every one of these four actions (Disable/Enable
        // Account, Resend/Cancel Invitation) changes AccountStatus/InvitationStatus once the
        // server round-trip and reload complete, which always swaps out or removes the action bar
        // entirely, so the exact button just clicked reliably stops being visible.
        await button.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }

    public async Task<string?> GetSuccessMessageAsync()
    {
        var alert = page.Locator(".alert-success");
        return await alert.IsVisibleAsync() ? await alert.InnerTextAsync() : null;
    }

    public async Task OpenManageRolesDialogAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Manage Roles" }).ClickAsync();
        await ManageRolesDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    private ILocator ManageRolesDialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Manage Roles" });

    /// <summary>
    /// Toggles the given roles (by name) in the checkbox-mode SfMultiSelect and saves. Toggling a
    /// role that's already selected deselects it, and vice versa — callers should pass only the
    /// roles they want to flip relative to the dialog's pre-populated <c>CurrentRoleIds</c>.
    /// </summary>
    public async Task ToggleRolesAndSaveAsync(IReadOnlyList<string> roleNamesToToggle)
    {
        await ManageRolesDialog.Locator("input[placeholder='Select one or more roles']").ClickAsync();
        await page.WaitForSelectorAsync(".e-popup:visible", new() { Timeout = 10_000 });

        foreach (var roleName in roleNamesToToggle)
        {
            await page.Locator(".e-popup .e-list-item")
                .Filter(new() { HasText = roleName })
                .First
                .ClickAsync();
        }

        // Checkbox-mode multiselect popups stay open to allow further selections, and being an
        // overlay it can sit visually on top of (and intercept clicks intended for) whatever sits
        // beneath it, including the dialog's own footer Save button — clicking a "neutral" element
        // underneath it is not reliable. Escape is the standard, unambiguous way to close a
        // Syncfusion dropdown/multiselect popup without depending on its rendered size/position.
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForSelectorAsync(".e-popup:visible", new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });

        await ManageRolesDialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await ManageRolesDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }

    public async Task<IReadOnlyList<string>> GetRoleNamesAsync()
    {
        await page.WaitForSelectorAsync(LoadedSelector, new() { Timeout = 15_000 });
        var card = page.Locator(".card", new() { HasText = "Account Details" });
        var badges = card.Locator(".badge.bg-secondary-subtle");
        var count = await badges.CountAsync();
        var names = new List<string>();
        for (var i = 0; i < count; i++)
            names.Add(await badges.Nth(i).InnerTextAsync());
        return names;
    }
}
