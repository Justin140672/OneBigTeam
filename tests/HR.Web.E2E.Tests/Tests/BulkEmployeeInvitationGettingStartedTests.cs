using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

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

        var href = await gettingStarted.GetTaskLinkUrlAsync("Invite your team");
        if (href is null)
        {
            return;
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
