using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the dashboard switcher (DashboardSwitcher.razor / AppSession.LandingUrl):
/// - It only appears for a user who qualifies for two or more of the three role dashboards
///   (HR, Recruitment, Manager) — CanManageCompany does NOT count towards this, so a
///   CompanyAdministrator + Manager combination (e.g. Sarah Chen) does not see it.
/// - A single-role user (e.g. Laura Bennett, HrAdministrator only) does not see it either.
/// - Switching dashboards navigates and persists the choice via localStorage ("lastDashboard"),
///   so reloading "/" redirects back to the last-chosen dashboard instead of the default
///   priority order (HR before Manager).
///
/// Uses David Park (david.park@acme.example), seeded with HrAdministrator + Manager roles
/// specifically to exercise this scenario — he also manages real direct reports (Emma Jones,
/// Carlos Rivera) so the Manager Dashboard he switches to isn't empty.
/// </summary>
[Collection("E2E")]
public sealed class DashboardSwitcherTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private const string HrAndManagerEmail = "david.park@acme.example";
    private const string HrOnlyEmail       = "laura.bennett@acme.example";

    [Fact]
    public async Task HrAndManagerUser_LandsOnHrDashboard_WithSwitcherShowingBothOptions()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAndManagerEmail);

        // HR beats Manager in the landing priority order (AppSession.LandingUrl).
        await _page.WaitForURLAsync(new Regex("/dashboard/hr"), new() { Timeout = 15_000 });

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
        await _page.WaitForURLAsync(new Regex("/dashboard/hr"), new() { Timeout = 15_000 });

        // ── Step 1: Switch to "My Team" (Manager Dashboard) via the switcher ──────
        var myTeamButton = _page.Locator(".dashboard-switcher-item").Filter(new() { HasText = "My Team" });
        await myTeamButton.ClickAsync();
        await _page.WaitForURLAsync(new Regex("/dashboard/manager"), new() { Timeout = 15_000 });

        // ── Step 2: Re-visiting "/" must redirect back to Manager, not the default
        // HR landing — proving the choice was persisted to localStorage ("lastDashboard"),
        // not just an in-memory navigation.
        await _page.GotoAsync($"{_fixture.WebBaseUrl}/");
        await _page.WaitForURLAsync(new Regex("/dashboard/manager"), new() { Timeout = 15_000 });

        Assert.Contains("/dashboard/manager", _page.Url);
    }

    [Fact]
    public async Task SingleRoleUser_DoesNotSeeSwitcher()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrOnlyEmail);
        await _page.WaitForURLAsync(new Regex("/dashboard/hr"), new() { Timeout = 15_000 });

        // Laura only satisfies one of the three switcher-eligible flags (IsHrAdministrator) —
        // the switcher must not render at all for her, same as any other single-role user.
        Assert.False(await _page.Locator(".dashboard-switcher").IsVisibleAsync(),
            "Expected no dashboard switcher for a single-role (HrAdministrator only) user");
    }
}
