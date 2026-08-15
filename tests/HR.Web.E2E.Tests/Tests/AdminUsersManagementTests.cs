using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies HR.Admin.Web's Administrators page (/admin-users), which lets a platform owner manage
/// PlatformAdministrator accounts (SupportStaff / PlatformOwner):
/// - An allow-listed admin can load the list.
/// - Creating a new administrator adds it to the list.
/// - Disabling then re-enabling an administrator flips its status pill both ways.
/// - Assigning a new role via the two-step (inline role picker -> AdminActionConfirmDialog) flow
///   updates the Role column.
/// - Reset MFA is a deliberate stub — its warning and success copy both say so explicitly (see
///   AdminUsers.razor's DialogWarning/_actionMessage for AdminUserAction.ResetMfa), asserted on
///   here so a future edit can't silently make it look like a real reset.
/// - Reset password shows a generic success message — HR.Admin.Web has no way to inspect
///   Supabase's outbound recovery email in this test setup, so (matching this suite's existing
///   precedent for other email-triggering actions, e.g. DeletionQueueTests) only the
///   ".admin-action-success" outcome is asserted, not delivery.
/// - Anonymous access to /admin-users redirects to /login, same pattern as
///   DeletionQueueTests.AnonymousAccess_ToDeletionQueue_RedirectsToLogin.
///
/// Uses "priya.shah@acme.example" as the allow-listed admin — same as DeletionQueueTests/
/// CustomerDetailsPageTests. That email is both in the "PlatformAdmin:AllowedEmails" config allow-
/// list AND bootstrap-seeded as an enabled PlatformOwner row by
/// IdentityModule.SeedPlatformAdministratorsFromConfigAsync on startup, so it should already be
/// authorised to view/manage this page without any additional E2E fixture changes.
///
/// Each test that creates an administrator uses a GUID-suffixed email so repeated runs against the
/// same fixture don't collide on the (assumed) unique-email constraint, and reuses that same
/// created row for the disable/enable/assign-role/reset-mfa/reset-password steps within the test
/// rather than creating a fresh administrator per action, to keep the list from growing unbounded
/// across repeated runs.
/// </summary>
[Collection("E2E")]
public sealed class AdminUsersManagementTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private const string AllowListedAdminEmail = "priya.shah@acme.example";

    private static string NewAdminEmail() => $"e2e-admin-{Guid.NewGuid():N}@example.test";

    [Fact]
    public async Task AdminUsers_AllowListedAdmin_LoadsList()
    {
        var login = new AdminLoginPage(_page, _fixture.AdminWebBaseUrl);
        var adminUsers = new AdminUsersPage(_page, _fixture.AdminWebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AllowListedAdminEmail);

        await adminUsers.GoToAsync();

        Assert.False(await adminUsers.IsErrorBannerVisibleAsync(),
            "Expected the allow-listed admin to see the administrators list, not the error banner");

        // The list should never truly be empty (the logged-in admin itself is seeded/created as a
        // row), but tolerate either the empty-state message or the table as a "list rendered"
        // outcome, matching DeletionQueueTests' convention.
        var isEmpty = await adminUsers.IsEmptyStateVisibleAsync();
        var hasTable = await adminUsers.IsTableVisibleAsync();
        Assert.True(isEmpty || hasTable,
            "Expected either the empty-state message or the administrators table to render");
    }

    [Fact]
    public async Task CreateAdministrator_AppearsInListWithSelectedRole()
    {
        var login = new AdminLoginPage(_page, _fixture.AdminWebBaseUrl);
        var adminUsers = new AdminUsersPage(_page, _fixture.AdminWebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AllowListedAdminEmail);

        await adminUsers.GoToAsync();

        var email = NewAdminEmail();
        await adminUsers.CreateAdministratorAsync(email, "SupportStaff");

        Assert.True(await adminUsers.HasAdministratorAsync(email),
            "Expected the newly-created administrator to appear in the list");
        Assert.True(await adminUsers.IsEnabledAsync(email),
            "Expected a newly-created administrator to be Enabled");

        var roleText = await adminUsers.GetRoleTextAsync(email) ?? "";
        Assert.Contains("SupportStaff", roleText);
    }

    [Fact]
    public async Task DisableThenEnableAdministrator_FlipsStatusPillBothWays()
    {
        var login = new AdminLoginPage(_page, _fixture.AdminWebBaseUrl);
        var adminUsers = new AdminUsersPage(_page, _fixture.AdminWebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AllowListedAdminEmail);

        await adminUsers.GoToAsync();

        var email = NewAdminEmail();
        await adminUsers.CreateAdministratorAsync(email, "SupportStaff");
        Assert.True(await adminUsers.HasAdministratorAsync(email));

        await adminUsers.ClickDisableAsync(email);
        Assert.True(await adminUsers.DisableDialog.IsVisibleAsync(),
            "Expected the Disable administrator confirmation dialog to open");

        await adminUsers.FillDialogReasonAsync(adminUsers.DisableDialog, "E2E: disabling newly-created administrator");
        await adminUsers.ClickDialogConfirmAsync(adminUsers.DisableDialog, "Disable");

        await _page.WaitForSelectorAsync(".admin-action-success", new() { Timeout = 15_000 });
        Assert.True(await adminUsers.IsDisabledAsync(email),
            "Expected the administrator's status pill to show Disabled after disabling");

        await adminUsers.ClickEnableAsync(email);
        Assert.True(await adminUsers.EnableDialog.IsVisibleAsync(),
            "Expected the Enable administrator confirmation dialog to open");

        await adminUsers.FillDialogReasonAsync(adminUsers.EnableDialog, "E2E: re-enabling the administrator");
        await adminUsers.ClickDialogConfirmAsync(adminUsers.EnableDialog, "Enable");

        await _page.WaitForSelectorAsync(".admin-action-success", new() { Timeout = 15_000 });
        Assert.True(await adminUsers.IsEnabledAsync(email),
            "Expected the administrator's status pill to show Enabled again after re-enabling");
    }

    [Fact]
    public async Task AssignRole_TwoStepFlow_UpdatesRoleColumn()
    {
        var login = new AdminLoginPage(_page, _fixture.AdminWebBaseUrl);
        var adminUsers = new AdminUsersPage(_page, _fixture.AdminWebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AllowListedAdminEmail);

        await adminUsers.GoToAsync();

        var email = NewAdminEmail();
        await adminUsers.CreateAdministratorAsync(email, "SupportStaff");
        Assert.True(await adminUsers.HasAdministratorAsync(email));

        await adminUsers.ClickAssignRoleAsync(email);
        Assert.True(await adminUsers.IsAssignRolePanelVisibleAsync(),
            "Expected the inline role picker panel to open");

        await adminUsers.SelectNewRoleAndContinueAsync("PlatformOwner");

        Assert.True(await adminUsers.AssignRoleDialog.IsVisibleAsync(),
            "Expected the Assign role confirmation dialog to open after continuing from the role picker");

        await adminUsers.FillDialogReasonAsync(adminUsers.AssignRoleDialog, "E2E: promoting administrator to PlatformOwner");
        await adminUsers.ClickDialogConfirmAsync(adminUsers.AssignRoleDialog, "Assign role");

        await _page.WaitForSelectorAsync(".admin-action-success", new() { Timeout = 15_000 });

        var roleText = await adminUsers.GetRoleTextAsync(email) ?? "";
        Assert.Contains("PlatformOwner", roleText);
    }

    [Fact]
    public async Task ResetMfa_IsStub_WarningAndSuccessMessageSayNoRealResetPerformed()
    {
        var login = new AdminLoginPage(_page, _fixture.AdminWebBaseUrl);
        var adminUsers = new AdminUsersPage(_page, _fixture.AdminWebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AllowListedAdminEmail);

        await adminUsers.GoToAsync();

        var email = NewAdminEmail();
        await adminUsers.CreateAdministratorAsync(email, "SupportStaff");
        Assert.True(await adminUsers.HasAdministratorAsync(email));

        await adminUsers.ClickResetMfaAsync(email);
        Assert.True(await adminUsers.ResetMfaDialog.IsVisibleAsync(),
            "Expected the Reset MFA confirmation dialog to open");

        // Deliberate wording assertion: this action is a stub, and the warning must say so
        // explicitly rather than implying a real Supabase MFA reset happens — see
        // AdminUsers.razor's DialogWarning for AdminUserAction.ResetMfa.
        var warningText = await adminUsers.GetDialogWarningTextAsync(adminUsers.ResetMfaDialog) ?? "";
        Assert.Contains("STUB", warningText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does NOT perform a real Supabase MFA reset", warningText, StringComparison.OrdinalIgnoreCase);

        await adminUsers.FillDialogReasonAsync(adminUsers.ResetMfaDialog, "E2E: resetting MFA (stub) for administrator");
        await adminUsers.ClickDialogConfirmAsync(adminUsers.ResetMfaDialog, "Reset MFA (stub)");

        await _page.WaitForSelectorAsync(".admin-action-success", new() { Timeout = 15_000 });

        var messageText = await adminUsers.GetActionMessageTextAsync() ?? "";
        Assert.Contains("no real MFA reset was performed", messageText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResetPassword_ShowsSuccessMessage()
    {
        var login = new AdminLoginPage(_page, _fixture.AdminWebBaseUrl);
        var adminUsers = new AdminUsersPage(_page, _fixture.AdminWebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(AllowListedAdminEmail);

        await adminUsers.GoToAsync();

        var email = NewAdminEmail();
        await adminUsers.CreateAdministratorAsync(email, "SupportStaff");
        Assert.True(await adminUsers.HasAdministratorAsync(email));

        await adminUsers.ClickResetPasswordAsync(email);
        Assert.True(await adminUsers.ResetPasswordDialog.IsVisibleAsync(),
            "Expected the Reset password confirmation dialog to open");

        await adminUsers.FillDialogReasonAsync(adminUsers.ResetPasswordDialog, "E2E: resetting password for administrator");
        await adminUsers.ClickDialogConfirmAsync(adminUsers.ResetPasswordDialog, "Reset password");

        await _page.WaitForSelectorAsync(".admin-action-success", new() { Timeout = 15_000 });
        Assert.True(await adminUsers.IsActionSuccessVisibleAsync(),
            "Expected a success message after resetting the administrator's password");
    }

    [Fact]
    public async Task AnonymousAccess_ToAdminUsers_RedirectsToLogin()
    {
        // Same pattern as DeletionQueueTests.AnonymousAccess_ToDeletionQueue_RedirectsToLogin:
        // navigate directly rather than via AdminUsersPage.GoToAsync, which waits for that page's
        // own settled-state selectors and would time out on /login.
        await _page.GotoAsync($"{_fixture.AdminWebBaseUrl}/admin-users");

        await _page.WaitForURLAsync(url => url.ToString().Contains("/login"), new() { Timeout = 20_000 });
    }
}
