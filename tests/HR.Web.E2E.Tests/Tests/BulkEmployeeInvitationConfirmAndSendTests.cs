using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

public sealed class BulkEmployeeInvitationConfirmAndSendTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private const string HrAdminEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task InviteMode_ConfirmAndSend_QueuesBatch_AndShowsProgressPanel()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var grid = new InviteModeCandidateGridPage(_page);
        var confirmDialog = new BulkInviteConfirmDialogPage(_page);
        var progressPanel = new InvitationBatchProgressPanelPage(_page);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        var (name, email) = (SeededE2eEmployees.BulkInvite[4].FullName, SeededE2eEmployees.BulkInvite[4].Email);

        await empList.GoToInviteModeAsync(AcmeId);
        await grid.WaitForLoadedAsync();

        await grid.ClickClearSelectionAsync();
        await grid.ToggleRowAsync(name);
        Assert.Equal(1, await grid.GetSelectedCountAsync());

        await grid.ClickInviteSelectedAsync();
        await confirmDialog.WaitForVisibleAsync();

        Assert.Contains("Employee", await confirmDialog.GetIntroTextAsync());
        var names = await confirmDialog.GetRecipientNamesAsync();
        var emails = await confirmDialog.GetRecipientEmailsAsync();
        Assert.Contains(names, n => n.Contains(name));
        Assert.Contains(emails, e => e.Equals(email, StringComparison.OrdinalIgnoreCase));

        await confirmDialog.SendAsync();
        Assert.False(await confirmDialog.IsVisibleAsync());

        await progressPanel.WaitForVisibleAsync();
        Assert.True(await progressPanel.HasProcessingContinuesMessageAsync()
            || (await progressPanel.GetStatusLabelAsync()).Contains("Completed"),
            "Expected the progress panel to show either the 'processing continues' hint (Queued/Processing) or have already reached Completed");
    }
}
