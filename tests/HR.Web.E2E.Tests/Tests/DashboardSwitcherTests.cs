using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class DashboardSwitcherTests(ManagerPersonaFixture fixture) : RoleE2ETestBase<ManagerPersonaFixture>(fixture)
{
    private const string HrAndManagerEmail = "david.park@acme.example";
    private const string HrOnlyEmail       = "laura.bennett@acme.example";

    [Fact]
    public async Task HrAndManagerUser_LandsOnHrDashboard_WithSwitcherShowingBothOptions()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAndManagerEmail);

        await _page.WaitForURLAsync(new Regex("/dashboard/hr"), new() { Timeout = 30_000, WaitUntil = WaitUntilState.Commit });

        var switcher = _page.Locator(".dashboard-switcher");
        await switcher.WaitForAsync(new() { Timeout = 15_000 });

        var itemTexts = await switcher.Locator(".dashboard-switcher-item").AllTextContentsAsync();
        Assert.Contains(itemTexts, t => t.Trim() == "HR");
        Assert.Contains(itemTexts, t => t.Trim() == "My Team");
        Assert.DoesNotContain(itemTexts, t => t.Trim() == "Recruitment");
        Assert.Equal(2, itemTexts.Count);
    }

    [Fact]
    public async Task SwitchingToManagerDashboard_NavigatesAndPersistsAcrossReload()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAndManagerEmail);
        await _page.WaitForURLAsync(new Regex("/dashboard/hr"), new() { Timeout = 30_000, WaitUntil = WaitUntilState.Commit });

        var myTeamButton = _page.Locator(".dashboard-switcher-item").Filter(new() { HasText = "My Team" });
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            await myTeamButton.ClickAsync();
            try
            {
                await _page.WaitForURLAsync(new Regex("/dashboard/manager"), new() { Timeout = 8_000, WaitUntil = WaitUntilState.Commit });
                break;
            }
            catch (TimeoutException) when (attempt < 4)
            {
            }
        }

        await _page.WaitForURLAsync(new Regex("/dashboard/manager"), new() { Timeout = 30_000, WaitUntil = WaitUntilState.Commit });

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/", new() { WaitUntil = WaitUntilState.Commit });
        await _page.WaitForURLAsync(new Regex("/dashboard/manager"), new() { Timeout = 30_000, WaitUntil = WaitUntilState.Commit });

        Assert.Contains("/dashboard/manager", _page.Url);
    }

    [Fact]
    public async Task SingleRoleUser_DoesNotSeeSwitcher()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrOnlyEmail);
        await _page.WaitForURLAsync(new Regex("/dashboard/hr"), new() { Timeout = 30_000, WaitUntil = WaitUntilState.Commit });

        // Laura only satisfies one of the three switcher-eligible flags (IsHrAdministrator) —
        // the switcher must not render at all for her, same as any other single-role user.
        Assert.False(await _page.Locator(".dashboard-switcher").IsVisibleAsync(),
            "Expected no dashboard switcher for a single-role (HrAdministrator only) user");
    }
}
