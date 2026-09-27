using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Split out of BulkEmployeeInvitationTests for real cross-test parallelism — see
/// BulkEmployeeInvitationGettingStartedTests' remarks.
///
/// The "Invite selected (N)" toolbar action also works from the NORMAL (non-invite-mode) grid
/// with a manual multi-row selection — not just the dedicated invitation-mode grid. Complements
/// EmployeeUserAccountColumnTests (individual row Quick Invite, still covered there) and
/// EmployeeListBulkUpdateTests (compensation bulk-update, unaffected by this feature) — this test
/// is scoped to the one new toolbar action neither of those covers.
/// </summary>
public sealed class BulkEmployeeInvitationManualSelectionTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private const string HrAdminEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task NormalGrid_ManualMultiSelection_InviteSelected_QueuesEligibleRecipients()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var confirmDialog = new BulkInviteConfirmDialogPage(_page);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        var (name, email) = (SeededE2eEmployees.BulkInvite[7].FullName, SeededE2eEmployees.BulkInvite[7].Email);

        await empList.GoToAsync(AcmeId);

        Assert.True(await empList.IsInviteSelectedToolbarButtonDisabledAsync(),
            "Expected 'Invite selected' to be disabled with no rows selected");

        await empList.CheckEmployeeRowAsync(name);

        Assert.True(await empList.WaitForInviteSelectedToolbarButtonEnabledAsync(),
            "Expected 'Invite selected' to be enabled once a row is selected");

        await empList.ClickInviteSelectedToolbarButtonAsync();
        await confirmDialog.WaitForVisibleAsync();

        var names = await confirmDialog.GetRecipientNamesAsync();
        var emails = await confirmDialog.GetRecipientEmailsAsync();
        Assert.Contains(names, n => n.Contains(name));
        Assert.Contains(emails, e => e.Equals(email, StringComparison.OrdinalIgnoreCase));

        await confirmDialog.SendAsync();

        var successMessage = await empList.GetActionSuccessMessageAsync();
        Assert.Contains("queued", successMessage, StringComparison.OrdinalIgnoreCase);
    }
}
