using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

public sealed class BulkEmployeeInvitationDeselectTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private const string HrAdminEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task InviteMode_DeselectingRow_ReducesCount_AndExcludesFromConfirmDialog()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var grid = new InviteModeCandidateGridPage(_page);
        var confirmDialog = new BulkInviteConfirmDialogPage(_page);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        var keepName = SeededE2eEmployees.BulkInvite[1].FullName;
        var dropName = SeededE2eEmployees.BulkInvite[2].FullName;

        await empList.GoToInviteModeAsync(AcmeId);
        await grid.WaitForLoadedAsync();

        Assert.True(await grid.IsRowCheckedAsync(keepName));
        Assert.True(await grid.IsRowCheckedAsync(dropName));

        var countBefore = await grid.GetSelectedCountAsync();
        await grid.ToggleRowAsync(dropName);
        var countAfter = await grid.GetSelectedCountAsync();

        Assert.Equal(countBefore - 1, countAfter);
        Assert.False(await grid.IsRowCheckedAsync(dropName));
        Assert.True(await grid.IsRowCheckedAsync(keepName));

        await grid.ClickInviteSelectedAsync();
        await confirmDialog.WaitForVisibleAsync();

        var names = await confirmDialog.GetRecipientNamesAsync();
        Assert.Contains(names, n => n.Contains(keepName));
        Assert.DoesNotContain(names, n => n.Contains(dropName));

        await confirmDialog.CancelAsync();
    }
}
