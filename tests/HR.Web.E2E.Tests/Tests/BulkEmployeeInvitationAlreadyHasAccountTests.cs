using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

public sealed class BulkEmployeeInvitationAlreadyHasAccountTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private const string HrAdminEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task NormalGrid_ManualSelection_AlreadyHasAccountEmployee_IsExcludedFromConfirmDialog()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var confirmDialog = new BulkInviteConfirmDialogPage(_page);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        await empList.GoToAsync(AcmeId);
        await empList.CheckEmployeeRowAsync("Laura Bennett");
        await empList.ClickInviteSelectedToolbarButtonAsync();
        await confirmDialog.WaitForVisibleAsync();

        Assert.True(await confirmDialog.HasExcludedSectionAsync(),
            "Expected 'Laura Bennett' (already has an active account) to be excluded rather than queued");

        var excluded = await confirmDialog.GetExcludedRowsAsync();
        Assert.Contains(excluded, r => r.Name.Contains("Bennett") && r.Reason.Contains("account"));

        var names = await confirmDialog.GetRecipientNamesAsync();
        Assert.DoesNotContain(names, n => n.Contains("Bennett"));

        await confirmDialog.CancelAsync();
    }
}
