using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

public sealed class BulkEmployeeInvitationSelectAllTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private const string HrAdminEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task InviteMode_SelectAllEligible_And_ClearSelection_UpdateCountAndButtons()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var grid = new InviteModeCandidateGridPage(_page);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        _ = SeededE2eEmployees.BulkInvite[3];

        await empList.GoToInviteModeAsync(AcmeId);
        await grid.WaitForLoadedAsync();

        var eligibleCount = await grid.GetSelectedCountAsync();
        Assert.True(eligibleCount > 0, "Expected at least one eligible candidate pre-selected");
        Assert.False(await grid.IsInviteSelectedButtonDisabledAsync(),
            "Expected 'Invite selected (N)' to be enabled while N > 0");

        await grid.ClickClearSelectionAsync();
        Assert.Equal(0, await grid.GetSelectedCountAsync());
        Assert.True(await grid.IsInviteSelectedButtonDisabledAsync(),
            "Expected 'Invite selected (N)' to be disabled once the selection count is 0");

        await grid.ClickSelectAllEligibleAsync();
        Assert.Equal(eligibleCount, await grid.GetSelectedCountAsync());
        Assert.False(await grid.IsInviteSelectedButtonDisabledAsync());
    }
}
