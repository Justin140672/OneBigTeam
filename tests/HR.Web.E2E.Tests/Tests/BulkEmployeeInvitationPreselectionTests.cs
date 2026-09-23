using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>Split out of BulkEmployeeInvitationTests for real cross-test parallelism — see BulkEmployeeInvitationGettingStartedTests' remarks.</summary>
public sealed class BulkEmployeeInvitationPreselectionTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private const string HrAdminEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task InviteMode_DirectNavigation_ShowsBannerAndPreselectsEligibleCandidates()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var grid = new InviteModeCandidateGridPage(_page);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        var candidate = SeededE2eEmployees.BulkInvite[0];

        await empList.GoToInviteModeAsync(AcmeId);

        Assert.True(await empList.IsInviteModeBannerVisibleAsync(),
            "Expected the invitation-mode banner on a direct ?mode=invite navigation");

        await grid.WaitForLoadedAsync();

        // A seeded pool employee with a valid work email and no linked account is eligible —
        // auto-selected on load (InviteModeCandidateGrid.EligibleIndexes/OnAfterRenderAsync).
        Assert.True(await grid.IsRowCheckedAsync(candidate.FullName),
            $"Expected eligible candidate '{candidate.FullName}' to be pre-selected");

        Assert.True(await grid.GetSelectedCountAsync() > 0,
            "Expected at least one candidate to be pre-selected");
    }
}
