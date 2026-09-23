using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Split out of BulkEmployeeInvitationTests (see that class's remarks for the full "bulk employee
/// invitations" feature context) so this test runs as its own xUnit collection — xUnit v2
/// parallelizes at the collection level, and every test WITHIN one class/collection always runs
/// sequentially against its siblings regardless of how fast each individual test is. The original
/// single 10-method class therefore queued its own tests one after another even after each was
/// individually made fast (pool-based arrange instead of UI-driven employee creation), matching
/// the same "many small classes, not one big one" convention already used by the ~139 role-fixed
/// test classes elsewhere in this suite specifically for this reason.
/// </summary>
public sealed class BulkEmployeeInvitationGettingStartedTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private const string HrAdminEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task GettingStarted_InviteYourTeamCard_NavigatesIntoInviteMode_WithReturnUrl()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var gettingStarted = new GettingStartedPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        await gettingStarted.GoToAsync();

        // InviteAdditionalUsersTask.IsCompletedAsync marks this task (and therefore hides its
        // Getting Started card) complete once EITHER Acme has >1 active user OR any invite has
        // EVER been sent for the company (EmailSentAt != null) — see that class's own remarks.
        // This suite's shared Acme company has many OTHER tests that send real invites (elsewhere
        // in this suite: EmployeeUserAccountColumnTests, InviteUserFromAdminTests, etc.), so
        // "no invite has ever been sent" cannot be guaranteed once the suite has been run before
        // against a persistent-within-run database — same "skip when the environment-dependent
        // precondition isn't met" convention already used elsewhere in this suite (e.g.
        // GettingStartedAndExploreTests.IncompleteTask_GoToTaskLink_...).
        var href = await gettingStarted.GetTaskLinkUrlAsync("Invite your team");
        if (href is null)
        {
            return; // Task already completed by earlier invite activity — nothing to assert.
        }

        Assert.Contains("mode=invite", href);
        Assert.Contains("returnUrl=", href);

        await gettingStarted.ClickTaskLinkAsync("Invite your team");

        await _page.WaitForURLAsync(new Regex($"/companies/{AcmeId}/employees"), new() { Timeout = 20_000 });
        Assert.Contains($"/companies/{AcmeId}/employees", _page.Url);
        Assert.Contains("mode=invite", _page.Url);

        Assert.True(await empList.IsInviteModeBannerVisibleAsync(),
            "Expected the invitation-mode banner to be visible after navigating from the Getting Started card");

        var backHref = await empList.GetBackToListLinkHrefAsync();
        Assert.NotNull(backHref);
        Assert.Contains("getting-started", Uri.UnescapeDataString(backHref!));
    }
}
