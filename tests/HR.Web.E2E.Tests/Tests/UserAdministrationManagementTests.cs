using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the "User Administration" area added in UserAdministrationList.razor / UserDetail.razor:
/// - The list page renders for an HR Administrator with expected grid data.
/// - A plain Employee has no nav link and is redirected away from the page (same access-control
///   convention as EmploymentTypeManagementTests / SicknessCategoryManagementTests).
/// - Inviting an eligible employee end-to-end via the Employee List's row-level "Invite User"
///   Quick Invite action (User Administration no longer has its own invite entry point — see
///   InviteUserDialog.razor), and the new row shows up with "Pending" invitation status.
/// - Navigating into a user's detail page and reading its audit history.
/// - Editing an active user's roles via "Manage Roles".
/// - Disabling then re-enabling an active user's account (the deactivate/reactivate coverage this
///   project's convention expects for every list+edit page pair).
///
/// Uses Laura Bennett (HR Administrator) and Tom Williams (plain Employee) against the seeded
/// Acme company, matching the personas described for this feature. "Emma Jones" and "Sophie
/// Laurent" are seeded Acme employees with no corresponding dev-persona user account (see
/// EmployeesModule's MakeAcme seed list vs. IdentityModule.SeedDevUserAsync's persona list), so
/// they are the "NoAccount" candidates the invite wizard's employee picker exercises.
/// </summary>
[Collection("E2E")]
public sealed class UserAdministrationManagementTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string HrAdminEmail = "laura.bennett@acme.example";
    private const string PlainEmployeeEmail = "tom.williams@acme.example";

    // Seeded Acme employee with no dev-persona user account, used as the invite target. If the
    // seed data ever changes so this employee gains an account, the Employee List's row-level
    // "Invite User" link will no longer render for them and
    // InviteEmployee_EndToEnd_ShowsPendingInvitation below will fail fast — pick a different
    // unlinked seeded employee at that point.
    private const string UninvitedEmployeeName = "Emma Jones";

    // Second seeded no-account employee (see the class doc comment) — a distinct invite target
    // for the Resend/Cancel toolbar tests below, so they don't collide with
    // InviteEmployee_EndToEnd_ShowsPendingInvitationInGrid's use of Emma Jones (which leaves her
    // permanently Pending on the shared dev database).
    private const string SecondUninvitedEmployeeName = "Sophie Laurent";

    [Fact]
    public async Task HrAdministrator_SeesUserAdministrationGrid()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var list  = new UserAdministrationListPage(_page, _fixture.WebBaseUrl);
        var sidebar = new SidebarPage(_page);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        Assert.True(await sidebar.HasTopLevelMenuItemAsync("User Administration"),
            "Expected the HR Administrator to see the 'User Administration' nav link");

        await list.GoToAsync(AcmeId);

        // Laura Bennett herself has an account and should appear in the grid.
        Assert.True(await list.HasRowAsync("Laura Bennett"),
            "Expected the User Administration grid to include Laura Bennett's own account row");
        Assert.True(await list.HasRowAsync("laura.bennett@acme.example"),
            "Expected the User Administration grid to show the user's email");
    }

    [Fact]
    public async Task PlainEmployee_HasNoNavLink_AndIsRedirectedAway_FromUserAdministrationPage()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var sidebar = new SidebarPage(_page);

        await login.GoToAsync();
        await login.LoginAsync(PlainEmployeeEmail);

        Assert.False(await sidebar.HasTopLevelMenuItemAsync("User Administration"),
            "A plain Employee should not see the 'User Administration' nav link");

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/user-administration");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15_000 });

        var finalUrl = _page.Url;
        Assert.False(finalUrl.Contains("/user-administration"),
            $"Expected a plain employee to be redirected away from the user administration page, but ended up at: {finalUrl}");
    }

    [Fact]
    public async Task InviteEmployee_EndToEnd_ShowsPendingInvitationInGrid()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var employees = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var list  = new UserAdministrationListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        // Inviting is only reachable from the Employee List's row-level "Invite User" action.
        await employees.GoToAsync(AcmeId);
        await employees.ClickInviteUserLinkAsync(UninvitedEmployeeName);

        // "Employee" is always applied automatically (fixed badge, not a selectable role) — no
        // additional roles are needed for this happy-path invite.
        await employees.CompleteQuickInviteAsync([]);

        await list.GoToAsync(AcmeId);

        Assert.True(await list.HasRowAsync(UninvitedEmployeeName),
            $"Expected '{UninvitedEmployeeName}' to appear in the User Administration grid after being invited");

        var invitationStatus = await list.GetInvitationStatusAsync(UninvitedEmployeeName);
        Assert.Equal("Pending", invitationStatus);
    }

    // ── Resend/Cancel Invitation toolbar actions ──────────────────────────────
    // UserAdministrationList.razor — the two toolbar buttons added alongside the standard
    // Add/Edit/View trio, selection-dependent like every other custom toolbar action
    // (SearchPageBase.AddToolbarAction), but the handlers themselves additionally guard on the
    // selected row actually having a pending/expired invite (HasActionableInvite) — an active
    // user has no InviteId left to act on, so clicking either button on one is a no-op with an
    // inline error rather than silently succeeding.

    [Fact]
    public async Task ResendThenCancelInvitation_FromToolbar_UpdatesInvitationStatus()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var employees = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var list  = new UserAdministrationListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        await employees.GoToAsync(AcmeId);
        await employees.ClickInviteUserLinkAsync(SecondUninvitedEmployeeName);
        await employees.CompleteQuickInviteAsync([]);

        await list.GoToAsync(AcmeId);
        Assert.Equal("Pending", await list.GetInvitationStatusAsync(SecondUninvitedEmployeeName));

        await list.SelectRowAsync(SecondUninvitedEmployeeName);
        await list.ClickResendInvitationAsync();

        Assert.Null(await list.GetActionErrorAsync());
        Assert.Equal("Pending", await list.GetInvitationStatusAsync(SecondUninvitedEmployeeName));

        await list.SelectRowAsync(SecondUninvitedEmployeeName);
        await list.ClickCancelInvitationAsync();

        Assert.Null(await list.GetActionErrorAsync());
        Assert.Equal("Cancelled", await list.GetInvitationStatusAsync(SecondUninvitedEmployeeName));
    }

    [Theory]
    [InlineData(true)]  // Resend
    [InlineData(false)] // Cancel
    public async Task InvitationToolbarAction_OnActiveUser_ShowsInlineError(bool resend)
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var list  = new UserAdministrationListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        await list.GoToAsync(AcmeId);
        // David Park is a seeded active account with no invite left to act on (see
        // UserDetail_ShowsAccountDetailsAndAuditHistory's own reasoning for using him as an
        // untouched-by-other-tests active persona).
        await list.SelectRowAsync("David Park");

        if (resend)
            await list.ClickResendInvitationAsync();
        else
            await list.ClickCancelInvitationAsync();

        var error = await list.GetActionErrorAsync();
        Assert.NotNull(error);
        Assert.Contains("does not have a pending invitation", error);
    }

    [Fact]
    public async Task UserDetail_ShowsAccountDetailsAndAuditHistory()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var list   = new UserAdministrationListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        await list.GoToAsync(AcmeId);
        // David Park is a seeded active account not otherwise edited by this test class — Marcus
        // Diallo is mutated by ManageRoles_UpdatesUsersRoles below, and reusing him here would make
        // the "starts with no audit history" assertion order-dependent on that other test.
        await list.OpenUserDetailAsync("David Park");

        var detail = new UserDetailPage(_page, _fixture.WebBaseUrl);

        Assert.Equal("Active", await detail.GetAccountStatusAsync());

        // Seeded dev personas carry no audit trail of their own (seeding bypasses the normal
        // audit-event flow), so the empty-history state is expected here — an actual edit is
        // what should produce the first entry.
        Assert.True(await detail.HasAuditHistoryEmptyMessageAsync(),
            "Expected a freshly seeded account to start with no audit history");

        await detail.OpenManageRolesDialogAsync();
        await detail.ToggleRolesAndSaveAsync(["Manager"]);
        Assert.Equal("Roles updated.", await detail.GetSuccessMessageAsync());

        Assert.False(await detail.HasAuditHistoryEmptyMessageAsync(),
            "Expected the account to have at least one audit history entry after a roles edit");
        Assert.True(await detail.GetAuditHistoryCountAsync() > 0,
            "Expected the roles edit to produce a visible audit history entry");
    }

    [Fact]
    public async Task ManageRoles_UpdatesUsersRoles()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var list  = new UserAdministrationListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        await list.GoToAsync(AcmeId);
        // Marcus Diallo is a seeded active Recruiter account — a safer target for a roles edit
        // than the HR Administrator persona actually driving the test.
        await list.OpenUserDetailAsync("Marcus Diallo");

        var detail = new UserDetailPage(_page, _fixture.WebBaseUrl);
        var rolesBefore = await detail.GetRoleNamesAsync();

        await detail.OpenManageRolesDialogAsync();
        // Toggle "Manager" on in addition to whatever roles Marcus already has, so the dialog's
        // "at least one role" guard is never at risk of being violated.
        await detail.ToggleRolesAndSaveAsync(["Manager"]);

        Assert.Equal("Roles updated.", await detail.GetSuccessMessageAsync());

        var rolesAfter = await detail.GetRoleNamesAsync();
        Assert.NotEqual(rolesBefore.OrderBy(r => r), rolesAfter.OrderBy(r => r));
        Assert.Contains("Manager", rolesAfter);
    }

    [Fact]
    public async Task DisableThenEnableAccount_UpdatesAccountStatus()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var list  = new UserAdministrationListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        await list.GoToAsync(AcmeId);
        // Use a seeded active account other than the HR Administrator persona driving the test,
        // so disabling it can't lock the test itself out mid-run.
        await list.OpenUserDetailAsync("Carlos Rivera");

        var detail = new UserDetailPage(_page, _fixture.WebBaseUrl);
        Assert.Equal("Active", await detail.GetAccountStatusAsync());

        await detail.DisableAccountAsync();
        Assert.Equal("Disabled", await detail.GetAccountStatusAsync());

        await detail.EnableAccountAsync();
        Assert.Equal("Active", await detail.GetAccountStatusAsync());
    }
}
