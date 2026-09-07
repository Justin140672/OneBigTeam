using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Interacts with UserDetail.razor ("/companies/{CompanyId}/user-administration/{EmployeeId}")
/// and the ManageUserRolesDialog.razor / UserAuditHistoryDialog.razor it opens via "Manage Roles"
/// / "View Audit History".
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
        try
        {
            await alert.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
        }
        catch (TimeoutException)
        {
            return null;
        }

        return await alert.InnerTextAsync();
    }

    public async Task OpenManageRolesDialogAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Manage Roles" }).ClickAsync();
        await ManageRolesDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    private ILocator ManageRolesDialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Manage Roles" });

    /// <summary>
    /// Toggles the given roles (by name) via the dialog's plain checkbox table and saves. Toggling
    /// a role that's already selected deselects it, and vice versa — callers should pass only the
    /// roles they want to flip relative to the dialog's pre-populated <c>CurrentRoleIds</c>.
    /// </summary>
    public async Task ToggleRolesAndSaveAsync(IReadOnlyList<string> roleNamesToToggle)
    {
        foreach (var roleName in roleNamesToToggle)
        {
            await ManageRolesDialog.Locator("tr", new() { HasText = roleName })
                .Locator("input[type='checkbox']")
                .First
                .ClickAsync();
        }

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

    private ILocator AuditHistoryDialog => page.GetByRole(AriaRole.Dialog, new() { Name = "Audit History" });

    public async Task OpenAuditHistoryDialogAsync()
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "View Audit History" }).ClickAsync();
        await AuditHistoryDialog.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    public async Task CloseAuditHistoryDialogAsync()
    {
        await AuditHistoryDialog.Locator(".audit-history-close-btn").ClickAsync();
        await AuditHistoryDialog.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 15_000 });
    }

    public async Task<bool> HasAuditHistoryEmptyMessageAsync()
    {
        var emptyMessage = AuditHistoryDialog.GetByText("No audit history recorded for this user.");
        var rows = AuditHistoryDialog.Locator("ul.list-unstyled > li");

        // The dialog loads its data asynchronously right after opening, so wait for whichever
        // end-state (empty message or at least one row) renders first before checking.
        try
        {
            await Task.WhenAny(
                emptyMessage.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 }),
                rows.First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 }));
        }
        catch (TimeoutException)
        {
            // Neither state appeared in time; fall through and report whatever is actually visible.
        }

        return await emptyMessage.IsVisibleAsync();
    }

    public async Task<int> GetAuditHistoryCountAsync() =>
        await AuditHistoryDialog.Locator("ul.list-unstyled > li").CountAsync();
}
