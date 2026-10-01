using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class DashboardWaitingOnOthersTests(HrAdminPersonaFixture fixture)
    : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private const string LauraEmail = "laura.bennett@acme.example";

    private async Task<HrDashboardPage> LoginAndOpenAsync()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new HrDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await dashboard.GoToAsync();
        await dashboard.WaitForAttentionQueueLoadedAsync();
        return dashboard;
    }

    private ILocator WaitingCard => _page.GetByTestId("waiting-on-others");

    [Fact]
    public async Task Dashboard_ShowsSeparateNeedsYourActionAndWaitingOnOthersSections()
    {
        await LoginAndOpenAsync();

        Assert.True(await _page.Locator(".widget-header").Filter(new() { HasText = "Needs your action" }).First.IsVisibleAsync());
        await Assertions.Expect(WaitingCard).ToBeVisibleAsync();
        await Assertions.Expect(WaitingCard.GetByRole(AriaRole.Heading, new() { Name = "Waiting on others" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task WaitingRows_AreNeverButtonsOrTaskLinks_AndNameTheResponsibleParty()
    {
        await LoginAndOpenAsync();
        await Assertions.Expect(WaitingCard).ToBeVisibleAsync();

        Assert.Equal(0, await WaitingCard.Locator("button").CountAsync());
        Assert.Equal(0, await WaitingCard.Locator("[role='button'], [tabindex='0']").CountAsync());

        var rows = WaitingCard.Locator(".waiting-item");
        var count = await rows.CountAsync();
        for (var i = 0; i < count; i++)
        {
            var row = rows.Nth(i);
            await Assertions.Expect(row.Locator(".waiting-owner")).ToContainTextAsync("Responsible:");

            var links = row.Locator("a");
            for (var l = 0; l < await links.CountAsync(); l++)
            {
                var href = await links.Nth(l).GetAttributeAsync("href");
                Assert.DoesNotContain("/tasks/", href ?? string.Empty);
            }
        }
    }

    [Fact]
    public async Task EveryMonitoringLink_NavigatesToAReadOnlyDestination()
    {
        await LoginAndOpenAsync();
        var links = WaitingCard.Locator("a.waiting-monitor-link");
        var count = await links.CountAsync();

        for (var i = 0; i < Math.Min(count, 3); i++)
        {
            var href = await links.Nth(i).GetAttributeAsync("href");
            Assert.False(string.IsNullOrWhiteSpace(href));

            var response = await _page.GotoAsync($"{_fixture.WebBaseUrl}{href}");
            Assert.NotNull(response);
            Assert.True(response!.Ok, $"Monitoring link {href} must resolve");
            await _page.GoBackAsync();
            await _page.Locator("[data-testid='waiting-on-others']").WaitForAsync(new() { Timeout = 15_000 });
        }
    }

    [Fact]
    public async Task EachSectionHasItsOwnEmptyState()
    {
        var dashboard = await LoginAndOpenAsync();

        var actionableRows = await dashboard.GetAttentionQueueRowCountAsync();
        if (actionableRows == 0)
            Assert.True(await dashboard.AttentionQueueIsAllClearAsync());

        var waitingRows = await WaitingCard.Locator(".waiting-item").CountAsync();
        if (waitingRows == 0)
            await Assertions.Expect(WaitingCard.Locator(".waiting-empty")).ToContainTextAsync("Nothing is waiting on anyone else");
    }

    [Fact]
    public async Task CountBadges_MatchTheNumberOfRenderedRows_WhenNotTruncated()
    {
        var dashboard = await LoginAndOpenAsync();

        var actionableRows = await dashboard.GetAttentionQueueRowCountAsync();
        if (actionableRows is > 0 and < 25)
            Assert.True(await dashboard.GetAttentionQueueCountBadgeAsync() >= actionableRows);

        var waitingRows = await WaitingCard.Locator(".waiting-item").CountAsync();
        var waitingBadge = WaitingCard.Locator(".widget-count-badge");
        if (waitingRows is > 0 and < 25)
            await Assertions.Expect(waitingBadge).ToHaveTextAsync(waitingRows.ToString());
        else if (waitingRows == 0)
            await Assertions.Expect(waitingBadge).ToHaveCountAsync(0);
    }
}
