using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class UserAdministrationManagementTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string HrAdminEmail = "laura.bennett@acme.example";
    private const string PlainEmployeeEmail = "tom.williams@acme.example";

    private static readonly string UninvitedEmployeeName = SeededE2eEmployees.QuickInvite.FullName;

    private async Task<string> CreateFreshUninvitedEmployeeAsync()
    {
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        var unique    = Guid.NewGuid().ToString("N")[..8];
        var lastName  = $"Invite{unique}";
        var workEmail = $"e2e.invite{unique}@acme.example";

        await empList.GoToAsync(AcmeId);
        await empList.ClickNewEmployeeAsync();
        await empEdit.FillFirstNameAsync("E2E");
        await empEdit.FillLastNameAsync(lastName);
        await empEdit.FillWorkEmailAsync(workEmail);
        await empEdit.FillRequiredAddressAsync();
        await empEdit.SelectDropdownAsync("Gender", "Male");
        await empEdit.SelectDropdownAsync("Nationality", "British");
        await empEdit.FillDateOfBirthAsync("15/06/1990");
        await empEdit.FillStartDateAsync("01/03/2026");
        await empEdit.FillEmployeeNumberAsync($"E2E-{unique}");
        await empEdit.SelectDropdownAsync("Employment Type", "Permanent");
        await empEdit.SelectDropdownAsync("Position Profile", "QA Engineer");
        await empEdit.SaveNewEmployeeAsync();

        return lastName;
    }

    [Fact]
    public async Task HrAdministrator_SeesUserAdministrationGrid()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var list  = new UserAdministrationListPage(_page, _fixture.WebBaseUrl);
        var sidebar = new SidebarPage(_page);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        Assert.True(await sidebar.HasGroupedMenuItemAsync("People and users", "User Administration"),
            "Expected the HR Administrator to see the 'User Administration' nav link under 'People and users'");

        await list.GoToAsync(AcmeId);

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
        await WaitForUrlToStopContainingAsync("/user-administration");

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

        await employees.GoToAsync(AcmeId);
        await employees.ClickInviteUserLinkAsync(UninvitedEmployeeName);

        await employees.CompleteQuickInviteAsync([]);

        await list.GoToAsync(AcmeId);

        Assert.True(await list.HasRowAsync(UninvitedEmployeeName),
            $"Expected '{UninvitedEmployeeName}' to appear in the User Administration grid after being invited");

        var invitationStatus = await list.GetInvitationStatusAsync(UninvitedEmployeeName);
        Assert.Equal("Pending", invitationStatus);
    }


    [Fact]
    public async Task ResendThenCancelInvitation_FromToolbar_UpdatesInvitationStatus()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var employees = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var list  = new UserAdministrationListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        var targetName = await CreateFreshUninvitedEmployeeAsync();

        await employees.GoToAsync(AcmeId);
        await employees.ClickInviteUserLinkAsync(targetName);
        await employees.CompleteQuickInviteAsync([]);

        await list.GoToAsync(AcmeId);
        Assert.Equal("Pending", await list.GetInvitationStatusAsync(targetName, "Pending"));

        await list.SelectRowAsync(targetName);
        await list.ClickResendInvitationAsync();

        Assert.Null(await list.GetActionErrorAsync());
        Assert.Equal("Pending", await list.GetInvitationStatusAsync(targetName, "Pending"));

        await list.SelectRowAsync(targetName);
        await list.ClickCancelInvitationAsync();

        Assert.Null(await list.GetActionErrorAsync());
        Assert.Equal("Cancelled", await list.GetInvitationStatusAsync(targetName, "Cancelled"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InvitationToolbarAction_OnActiveUser_ShowsInlineError(bool resend)
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var list  = new UserAdministrationListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        await list.GoToAsync(AcmeId);
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
        await list.OpenUserDetailAsync("Nina Patel");

        var detail = new UserDetailPage(_page, _fixture.WebBaseUrl);

        Assert.Equal("Active", await detail.GetAccountStatusAsync());

        var neededPreCleanup = (await detail.GetRoleNamesAsync()).Contains("Manager");
        if (neededPreCleanup)
        {
            await detail.OpenManageRolesDialogAsync();
            await detail.ToggleRolesAndSaveAsync(["Manager"]);
        }

        var rolesBefore = await detail.GetRoleNamesAsync();
        Assert.DoesNotContain("Manager", rolesBefore);

        await detail.OpenAuditHistoryDialogAsync();
        if (!neededPreCleanup)
        {
            Assert.True(await detail.HasAuditHistoryEmptyMessageAsync(),
                "Expected a freshly seeded account to start with no audit history");
        }
        await detail.CloseAuditHistoryDialogAsync();

        await detail.OpenManageRolesDialogAsync();
        await detail.ToggleRolesAndSaveAsync(["Manager"]);
        Assert.Equal("Roles updated.", await detail.GetSuccessMessageAsync());

        Assert.Contains("Manager", await detail.GetRoleNamesAsync());

        await detail.OpenAuditHistoryDialogAsync();
        Assert.False(await detail.HasAuditHistoryEmptyMessageAsync(),
            "Expected the account to have at least one audit history entry after a roles edit");
        Assert.True(await detail.GetAuditHistoryCountAsync() > 0,
            "Expected the roles edit to produce a visible audit history entry");
        await detail.CloseAuditHistoryDialogAsync();

        await detail.OpenManageRolesDialogAsync();
        await detail.ToggleRolesAndSaveAsync(["Manager"]);
        Assert.DoesNotContain("Manager", await detail.GetRoleNamesAsync());

        if (neededPreCleanup)
        {
            await detail.OpenManageRolesDialogAsync();
            await detail.ToggleRolesAndSaveAsync(["Manager"]);
            Assert.Contains("Manager", await detail.GetRoleNamesAsync());
        }
    }

    [Fact]
    public async Task ManageRoles_UpdatesUsersRoles()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var list  = new UserAdministrationListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        await list.GoToAsync(AcmeId);
        await list.OpenUserDetailAsync("Nina Patel");

        var detail = new UserDetailPage(_page, _fixture.WebBaseUrl);
        var rolesBefore = await detail.GetRoleNamesAsync();

        await detail.OpenManageRolesDialogAsync();
        await detail.ToggleRolesAndSaveAsync(["Manager"]);

        Assert.Equal("Roles updated.", await detail.GetSuccessMessageAsync());

        var rolesAfter = await detail.GetRoleNamesAsync();
        Assert.NotEqual(rolesBefore.OrderBy(r => r), rolesAfter.OrderBy(r => r));
        Assert.Equal(!rolesBefore.Contains("Manager"), rolesAfter.Contains("Manager"));

        await detail.OpenManageRolesDialogAsync();
        await detail.ToggleRolesAndSaveAsync(["Manager"]);
        Assert.Equal(rolesBefore.Contains("Manager"), (await detail.GetRoleNamesAsync()).Contains("Manager"));
    }

    [Fact]
    public async Task DisableThenEnableAccount_UpdatesAccountStatus()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var list  = new UserAdministrationListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        await list.GoToAsync(AcmeId);
        await list.OpenUserDetailAsync("Carlos Rivera");

        var detail = new UserDetailPage(_page, _fixture.WebBaseUrl);
        Assert.Equal("Active", await detail.GetAccountStatusAsync());

        await detail.DisableAccountAsync();
        Assert.Equal("Disabled", await detail.GetAccountStatusAsync());

        await detail.EnableAccountAsync();
        Assert.Equal("Active", await detail.GetAccountStatusAsync());
    }
}
