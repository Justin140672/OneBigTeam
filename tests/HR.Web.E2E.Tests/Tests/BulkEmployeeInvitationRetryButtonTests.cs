using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

public sealed class BulkEmployeeInvitationRetryButtonTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private const string HrAdminEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task ProgressPanel_RetryButton_NotShown_WhenNoFailedRecipients()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var grid = new InviteModeCandidateGridPage(_page);
        var confirmDialog = new BulkInviteConfirmDialogPage(_page);
        var progressPanel = new InvitationBatchProgressPanelPage(_page);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        var name = SeededE2eEmployees.BulkInvite[6].FullName;

        await empList.GoToInviteModeAsync(AcmeId);
        await grid.WaitForLoadedAsync();
        await grid.ClickClearSelectionAsync();
        await grid.ToggleRowAsync(name);
        await grid.ClickInviteSelectedAsync();
        await confirmDialog.WaitForVisibleAsync();
        await confirmDialog.SendAsync();

        await progressPanel.WaitForVisibleAsync();
        await progressPanel.WaitForCompletedAsync();

        if (await progressPanel.GetFailedCountAsync() == 0)
        {
            Assert.False(await progressPanel.HasRetryButtonAsync(),
                "Expected no 'Retry failed invitations' button while the batch has 0 Failed recipients");
        }
    }
}
