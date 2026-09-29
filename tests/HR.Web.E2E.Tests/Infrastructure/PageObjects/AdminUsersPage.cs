using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

public sealed class AdminUsersPage(IPage page, string baseUrl)
{
    private const string SettledSelector = ".dashboard-error, .activity-empty, table.billing-history-table";

    public async Task GoToAsync()
    {
        await page.GotoAsync($"{baseUrl}/admin-users");
        await page.WaitForSelectorAsync(SettledSelector, new() { Timeout = 20_000 });
    }

    public Task<bool> IsErrorBannerVisibleAsync() =>
        page.Locator(".dashboard-error").IsVisibleAsync();

    public Task<bool> IsEmptyStateVisibleAsync() =>
        page.Locator(".activity-empty").IsVisibleAsync();

    public Task<bool> IsTableVisibleAsync() =>
        page.Locator("table.billing-history-table").IsVisibleAsync();

    private ILocator RowByEmail(string emailFragment) =>
        page.Locator("table.billing-history-table tbody tr").Filter(new() { HasText = emailFragment });

    public async Task<bool> HasAdministratorAsync(string emailFragment)
    {
        await page.WaitForSelectorAsync(SettledSelector, new() { Timeout = 15_000 });
        return await RowByEmail(emailFragment).First.IsVisibleAsync();
    }

    // Wait (bounded) for the expected pill rather than snapshotting: AdminUsers.razor deliberately
    // renders the ".admin-action-success" banner BEFORE its post-action list reload, and that banner
    // also stays visible from a previous action — so "banner visible" never proves the row's status
    // pill has been re-rendered yet. Only used for positive assertions.
    public Task<bool> IsEnabledAsync(string emailFragment) =>
        WaitForVisibleAsync(RowByEmail(emailFragment).Locator(".status-pill-enabled"));

    public Task<bool> IsDisabledAsync(string emailFragment) =>
        WaitForVisibleAsync(RowByEmail(emailFragment).Locator(".status-pill-disabled"));

    private static async Task<bool> WaitForVisibleAsync(ILocator locator)
    {
        try
        {
            await locator.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 20_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task<string?> GetRoleTextAsync(string emailFragment, string? expectedRole = null)
    {
        var cell = RowByEmail(emailFragment).Locator("td").Nth(1);
        if (expectedRole is not null)
        {
            try
            {
                await Assertions.Expect(cell).ToContainTextAsync(expectedRole, new() { Timeout = 20_000 });
            }
            catch (PlaywrightException)
            {
            }
        }

        return await cell.TextContentAsync();
    }


    private ILocator CreatePanel => page.Locator(".admin-actions-panel").Filter(new() { HasText = "Create administrator" });

    public async Task CreateAdministratorAsync(string email, string role = "SupportStaff")
    {
        await page.Locator("#new-admin-email").FillAsync(email);
        await page.Keyboard.PressAsync("Tab");

        await DropDownSelector.SelectAsync(page, CreatePanel, role);

        await CreatePanel.GetByRole(AriaRole.Button, new() { Name = "Create administrator" }).ClickAsync();

        await page.Locator($"table.billing-history-table tbody tr:has-text('{email}'), .admin-action-error")
            .First.WaitForAsync(new() { Timeout = 15_000 });
    }

    public Task<string?> GetCreateErrorAsync() =>
        CreatePanel.Locator(".admin-action-error").TextContentAsync();


    public async Task ClickDisableAsync(string emailFragment)
    {
        await RowByEmail(emailFragment).GetByRole(AriaRole.Button, new() { Name = "Disable" }).ClickAsync();
        await DisableDialog.WaitForAsync(new() { Timeout = 15_000 });
    }

    public async Task ClickEnableAsync(string emailFragment)
    {
        await RowByEmail(emailFragment).GetByRole(AriaRole.Button, new() { Name = "Enable" }).ClickAsync();
        await EnableDialog.WaitForAsync(new() { Timeout = 15_000 });
    }

    public async Task ClickAssignRoleAsync(string emailFragment)
    {
        await RowByEmail(emailFragment).GetByRole(AriaRole.Button, new() { Name = "Assign role" }).ClickAsync();
        await AssignRolePanel.WaitForAsync(new() { Timeout = 15_000 });
    }

    public async Task ClickResetMfaAsync(string emailFragment)
    {
        await RowByEmail(emailFragment).GetByRole(AriaRole.Button, new() { Name = "Reset MFA" }).ClickAsync();
        await ResetMfaDialog.WaitForAsync(new() { Timeout = 15_000 });
    }

    public async Task ClickResetPasswordAsync(string emailFragment)
    {
        await RowByEmail(emailFragment).GetByRole(AriaRole.Button, new() { Name = "Reset password" }).ClickAsync();
        await ResetPasswordDialog.WaitForAsync(new() { Timeout = 15_000 });
    }


    private ILocator AssignRolePanel => page.Locator(".admin-actions-panel").Filter(new() { HasText = "Assign role for" });

    public Task<bool> IsAssignRolePanelVisibleAsync() => AssignRolePanel.IsVisibleAsync();

    public async Task SelectNewRoleAndContinueAsync(string role)
    {
        await DropDownSelector.SelectAsync(page, AssignRolePanel, role);
        await AssignRolePanel.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();
        await AssignRoleDialog.WaitForAsync(new() { Timeout = 15_000 });
    }


    public ILocator DialogByTitle(string title) => page.GetByRole(AriaRole.Dialog, new() { Name = title });

    public ILocator DisableDialog => DialogByTitle("Disable administrator");

    public ILocator EnableDialog => DialogByTitle("Enable administrator");

    public ILocator AssignRoleDialog => DialogByTitle("Assign role");

    public ILocator ResetMfaDialog => DialogByTitle("Reset MFA");

    public ILocator ResetPasswordDialog => DialogByTitle("Reset password");

    public Task<string?> GetDialogWarningTextAsync(ILocator dialog) =>
        dialog.Locator(".admin-action-warning").TextContentAsync();

    public async Task FillDialogReasonAsync(ILocator dialog, string reason)
    {
        await dialog.Locator("#admin-action-reason").FillAsync(reason);
        await page.Keyboard.PressAsync("Tab");
    }

    public Task ClickDialogConfirmAsync(ILocator dialog, string confirmButtonName) =>
        dialog.GetByRole(AriaRole.Button, new() { Name = confirmButtonName, Exact = true }).ClickAsync();

    public Task<string?> GetDialogValidationErrorAsync(ILocator dialog) =>
        dialog.Locator(".admin-action-error").TextContentAsync();


    public Task<bool> IsActionSuccessVisibleAsync() =>
        page.Locator(".admin-action-success").IsVisibleAsync();

    public Task<string?> GetActionMessageTextAsync() =>
        page.Locator(".admin-action-success, .admin-action-error").First.TextContentAsync();
}
