using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Page object for HR.Admin.Web's AdminUsers.razor (/admin-users) — the platform-owner-only page
/// for managing PlatformAdministrator accounts (SupportStaff / PlatformOwner). Disable, Enable,
/// Assign role, Reset MFA and Reset password all go through the shared AdminActionConfirmDialog
/// (mandatory reason, min 5 chars — see AdminActionConfirmDialog.razor's OnConfirmClicked),
/// addressed by its per-action Title the same way DeletionQueuePage does — see AdminUsers.razor's
/// DialogTitle switch: "Disable administrator", "Enable administrator", "Assign role",
/// "Reset MFA", "Reset password".
///
/// "Assign role" is a two-step flow: an inline ".admin-actions-panel" role picker
/// (#assign-role-select, a Syncfusion SfDropDownList) with its own "Continue" button, which then
/// opens the AdminActionConfirmDialog titled "Assign role" — see AdminUsers.razor's
/// OpenAssignRole/ConfirmAssignRoleSelection.
///
/// Reset MFA is a deliberate stub — its warning and (on success) its message both say so
/// explicitly (see AdminUsers.razor's DialogWarning/_actionMessage for AdminUserAction.ResetMfa) —
/// tests assert on that wording directly so a future edit can't silently make it look like a real
/// reset.
/// </summary>
public sealed class AdminUsersPage(IPage page, string baseUrl)
{
    // AdminUsers.razor always renders the create panel; below that it renders exactly one of:
    // loading text, the "not authorised" dashboard-error div, the empty-state paragraph, or the
    // administrators table — wait for any "settled" state.
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

    public Task<bool> IsEnabledAsync(string emailFragment) =>
        RowByEmail(emailFragment).Locator(".status-pill-enabled").IsVisibleAsync();

    public Task<bool> IsDisabledAsync(string emailFragment) =>
        RowByEmail(emailFragment).Locator(".status-pill-disabled").IsVisibleAsync();

    public Task<string?> GetRoleTextAsync(string emailFragment)
    {
        // Columns are Email, Role, Status, Created, actions — Role is the 2nd <td>.
        var cell = RowByEmail(emailFragment).Locator("td").Nth(1);
        return cell.TextContentAsync();
    }

    // --- Create administrator panel ---

    private ILocator CreatePanel => page.Locator(".admin-actions-panel").Filter(new() { HasText = "Create administrator" });

    public async Task CreateAdministratorAsync(string email, string role = "SupportStaff")
    {
        await page.Locator("#new-admin-email").FillAsync(email);
        // SfTextBox's server-side bound value only round-trips on blur/change, not on FillAsync's
        // raw "input" DOM event alone — same convention as AdminLoginPage.LoginAsync.
        await page.Keyboard.PressAsync("Tab");

        await DropDownSelector.SelectAsync(page, CreatePanel, role);

        await CreatePanel.GetByRole(AriaRole.Button, new() { Name = "Create administrator" }).ClickAsync();
    }

    public Task<string?> GetCreateErrorAsync() =>
        CreatePanel.Locator(".admin-action-error").TextContentAsync();

    // --- Row actions ---

    public Task ClickDisableAsync(string emailFragment) =>
        RowByEmail(emailFragment).GetByRole(AriaRole.Button, new() { Name = "Disable" }).ClickAsync();

    public Task ClickEnableAsync(string emailFragment) =>
        RowByEmail(emailFragment).GetByRole(AriaRole.Button, new() { Name = "Enable" }).ClickAsync();

    public Task ClickAssignRoleAsync(string emailFragment) =>
        RowByEmail(emailFragment).GetByRole(AriaRole.Button, new() { Name = "Assign role" }).ClickAsync();

    public Task ClickResetMfaAsync(string emailFragment) =>
        RowByEmail(emailFragment).GetByRole(AriaRole.Button, new() { Name = "Reset MFA" }).ClickAsync();

    public Task ClickResetPasswordAsync(string emailFragment) =>
        RowByEmail(emailFragment).GetByRole(AriaRole.Button, new() { Name = "Reset password" }).ClickAsync();

    // --- Inline "Assign role" picker panel (step 1 of the two-step Assign role flow) ---

    private ILocator AssignRolePanel => page.Locator(".admin-actions-panel").Filter(new() { HasText = "Assign role for" });

    public Task<bool> IsAssignRolePanelVisibleAsync() => AssignRolePanel.IsVisibleAsync();

    public async Task SelectNewRoleAndContinueAsync(string role)
    {
        await DropDownSelector.SelectAsync(page, AssignRolePanel, role);
        await AssignRolePanel.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();
    }

    // --- Shared AdminActionConfirmDialog, addressed by its per-action title ---

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

    // --- Post-action feedback (below the table) ---

    public Task<bool> IsActionSuccessVisibleAsync() =>
        page.Locator(".admin-action-success").IsVisibleAsync();

    public Task<string?> GetActionMessageTextAsync() =>
        page.Locator(".admin-action-success, .admin-action-error").First.TextContentAsync();
}
