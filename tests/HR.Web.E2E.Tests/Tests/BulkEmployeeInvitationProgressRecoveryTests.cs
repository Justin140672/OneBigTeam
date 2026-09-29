using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

public sealed class BulkEmployeeInvitationProgressRecoveryTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private const string HrAdminEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task InviteMode_BatchProgress_SurvivesRefresh_AndReachesTerminalState()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var grid = new InviteModeCandidateGridPage(_page);
        var confirmDialog = new BulkInviteConfirmDialogPage(_page);
        var progressPanel = new InvitationBatchProgressPanelPage(_page);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        var name = SeededE2eEmployees.BulkInvite[5].FullName;

        await empList.GoToInviteModeAsync(AcmeId);
        await grid.WaitForLoadedAsync();
        await grid.ClickClearSelectionAsync();
        await grid.ToggleRowAsync(name);

        await grid.ClickInviteSelectedAsync();
        await confirmDialog.WaitForVisibleAsync();
        await confirmDialog.SendAsync();

        await progressPanel.WaitForVisibleAsync();

        await _page.ReloadAsync();
        await empList.IsInviteModeBannerVisibleAsync();
        await progressPanel.WaitForVisibleAsync();

        await progressPanel.WaitForCompletedAsync();

        var status = await progressPanel.GetStatusLabelAsync();
        Assert.Contains("Completed", status);

        var sent = await progressPanel.GetSentCountAsync();
        var skipped = await progressPanel.GetSkippedCountAsync();
        var failed = await progressPanel.GetFailedCountAsync();
        Assert.True(sent + skipped + failed >= 1,
            "Expected the completed batch to account for at least the one recipient queued");
    }
}
