using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class EmployeeUserAccountColumnTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string HrAdminEmail = "laura.bennett@acme.example";

    private const string ActiveEmployeeName = "Laura Bennett";
    private const string DisableTargetEmployeeName = "Carlos Rivera";
    private const string NoUserEmployeeName = "Laurent";

    [Fact]
    public async Task UserAccountColumn_ShowsActiveIconAndLabel_ForEmployeeWithActiveAccount()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var list = new EmployeeListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);
        await list.GoToAsync(AcmeId);

        var text = await list.GetUserAccountStatusTextAsync(ActiveEmployeeName);
        Assert.Contains("Active", text);

        var iconClass = await list.GetUserAccountStatusIconClassAsync(ActiveEmployeeName);
        Assert.Contains("fa-circle-check", iconClass);
    }

    [Fact]
    public async Task UserAccountColumn_ShowsNoUserIconLabelAndInviteLink_ForEmployeeWithoutAccount()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var list = new EmployeeListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);
        await list.GoToAsync(AcmeId);

        var text = await list.GetUserAccountStatusTextAsync(NoUserEmployeeName);
        Assert.Contains("No account", text);

        var iconClass = await list.GetUserAccountStatusIconClassAsync(NoUserEmployeeName);
        Assert.Contains("fa-circle-minus", iconClass);

        Assert.True(await list.HasInviteUserLinkAsync(NoUserEmployeeName),
            "Expected the 'Invite' action on a row with no linked user account");
    }

    [Fact]
    public async Task UserAccountColumn_InviteLink_NotShown_ForEmployeeWithActiveAccount()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var list = new EmployeeListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);
        await list.GoToAsync(AcmeId);

        Assert.False(await list.HasInviteUserLinkAsync(ActiveEmployeeName),
            "The 'Invite' action should only be offered for 'No account' rows, not Active ones");
    }

    [Fact]
    public async Task UserAccountColumn_ShowsDisabledIconAndLabel_ForDisabledAccount()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var list = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var userAdminList = new UserAdministrationListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        await userAdminList.GoToAsync(AcmeId);
        await userAdminList.OpenUserDetailAsync(DisableTargetEmployeeName);
        var detail = new UserDetailPage(_page, _fixture.WebBaseUrl);
        Assert.Equal("Active", await detail.GetAccountStatusAsync());
        await detail.DisableAccountAsync();
        Assert.Equal("Disabled", await detail.GetAccountStatusAsync());

        try
        {
            await list.GoToAsync(AcmeId);

            var text = await list.GetUserAccountStatusTextAsync(DisableTargetEmployeeName);
            Assert.Contains("Disabled", text);

            var iconClass = await list.GetUserAccountStatusIconClassAsync(DisableTargetEmployeeName);
            Assert.Contains("fa-ban", iconClass);

            Assert.False(await list.HasInviteUserLinkAsync(DisableTargetEmployeeName),
                "The 'Invite' action should not be offered for a Disabled row");
        }
        finally
        {
            await userAdminList.GoToAsync(AcmeId);
            await userAdminList.OpenUserDetailAsync(DisableTargetEmployeeName);
            if (await detail.GetAccountStatusAsync() == "Disabled")
                await detail.EnableAccountAsync();
        }
    }

    // NOTE: column sorting and Excel-style filtering on the "User Account" column are not covered
    // here — those are Syncfusion grid behaviours, not our own logic, and asserting them just
    // tests the third-party control.

    [Fact]
    public async Task ExportButton_IsPresentAndEnabled_WithUserAccountColumnInGrid()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var list = new EmployeeListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);
        await list.GoToAsync(AcmeId);

        Assert.True(await _page.Locator(".e-headercell").Filter(new() { HasText = "User Account" }).IsVisibleAsync());

        var moreButton = _page.GetByRole(AriaRole.Button, new() { Name = "More actions" });
        Assert.True(await moreButton.IsVisibleAsync());
        Assert.False(await moreButton.IsDisabledAsync());

        await moreButton.ClickAsync();
        var exportToExcelItem = _page.Locator("#hr-excel");
        await exportToExcelItem.WaitForAsync(new() { Timeout = 10_000 });
        Assert.True(await exportToExcelItem.IsVisibleAsync());

        // NOTE: whether the exported Excel/CSV/PDF file's contents actually include the User
        // Account column can't practically be asserted here — Playwright would need to download
        // and parse a binary spreadsheet/PDF, which this suite's conventions avoid faking. This
        // test is limited to confirming the export entrypoint is present/enabled while the column
        // itself is part of the grid it operates on.
    }

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
        await empEdit.FillRequiredCompensationAsync();
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
    public async Task QuickInvite_ForNoUserEmployee_OpensPreselectedDialog_AndCompletesToPendingInvitation()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var list = new EmployeeListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        var targetName = await CreateFreshUninvitedEmployeeAsync();

        await list.GoToAsync(AcmeId);

        Assert.True(await list.HasInviteUserLinkAsync(targetName),
            $"Expected '{targetName}' (freshly created with no linked user account) to show the 'Invite User' link");

        await list.ClickInviteUserLinkAsync(targetName);

        await list.CompleteQuickInviteAsync([]);

        Assert.Contains("/employees", _page.Url);
        Assert.DoesNotContain("/user-administration", _page.Url);

        var successMessage = await list.GetActionSuccessMessageAsync();
        Assert.Equal("Invitation sent.", successMessage);

        var text = await list.GetUserAccountStatusTextAsync(targetName);
        Assert.Contains("Invited", text);

        Assert.False(await list.HasInviteUserLinkAsync(targetName),
            "The 'Invite' action should no longer be offered once the employee has a pending invitation");
    }
}
