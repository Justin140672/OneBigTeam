using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Split out of BulkEmployeeInvitationTests for real cross-test parallelism — see
/// BulkEmployeeInvitationGettingStartedTests' remarks.
///
/// A deterministic Skipped outcome: select an employee via the NORMAL grid's manual-selection
/// "Invite selected" path who already has an active user account (Laura Bennett herself, the
/// logged-in HR Administrator) — EmployeeList.OnInviteSelectedClicked classifies this client-side
/// as "AlreadyHasAccount" and excludes her from the recipient list entirely before any batch is
/// even queued, mirroring the server-side exclusion reasons. This does not reach
/// InvitationBatchProgressPanel's own Skipped/Failed counts (those only apply to recipients the
/// server actually attempted to email) — it is covered here as the deterministic,
/// environment-independent equivalent: this suite avoids faking a real email-send failure (the
/// email sender is a live/fake network dependency whose failure mode isn't reproducible on
/// demand), so the "excluded reasons shown before send" level is the correct place to assert this
/// deterministically, consistent with how EmployeeUserAccountColumnTests documents that "Invite
/// expired" (a genuinely time-based state) isn't reachable through the UI either.
/// </summary>
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
        await empList.CheckEmployeeRowAsync("Laura Bennett"); // Active account already
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
