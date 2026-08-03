using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies that a CompanyAdministrator-only user (CanManageCompany true, CanManageEmployees
/// false — no other HR role) is scoped to the Company edit screen only:
/// - Landing on "/" redirects straight to the Company edit page (Home.razor).
/// - The sidebar (MainLayout.razor) shows only the "Company" menu item, not the full HR menu.
/// - Backend access to the employee list is still denied even via direct navigation, proving
///   the narrowing isn't just a hidden UI affordance.
///
/// Contrasted against an HrAdministrator (CanManageEmployees true), who should still see the
/// full sidebar and land on her role-specific dashboard ("/dashboard/hr") rather than the
/// Company edit screen — unaffected by this change.
/// </summary>
[Collection("E2E")]
public sealed class CompanyAdministratorAccessTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    // Priya has the CompanyAdministrator role and NO other role — CanManageEmployees is false,
    // CanManageCompany is true. Same persona used by CompanySettingsTests / CompanyEditCloseBehaviorTests.
    private const string CompanyAdminEmail = "priya.shah@acme.example";

    // Laura has the HrAdministrator role (CanManageEmployees true) — used here only as a
    // before/after contrast to confirm the full-HR sidebar/dashboard is unaffected.
    private const string HrAdminEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task CompanyAdministrator_RedirectedFromRoot_ToCompanyEdit()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        // ── Step 1: Login as Priya (CompanyAdministrator-only) ────────────────
        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

        // ── Step 2: Login lands on "/" by default; Home.razor's OnInitializedAsync
        // immediately issues a client-side NavigateTo(replace: true) to the Company edit
        // screen once it sees CanManageCompany && !CanManageEmployees. Wait for that
        // navigation to settle rather than asserting the URL immediately.
        await _page.WaitForURLAsync(new Regex($"/companies/{AcmeId}/edit"), new() { Timeout = 15_000 });

        Assert.Contains($"/companies/{AcmeId}/edit", _page.Url);
    }

    [Fact]
    public async Task CompanyAdministrator_SeesOnlyCompanyMenuItem_InSidebar()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        // ── Step 1: Login as Priya (CompanyAdministrator-only) ────────────────
        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

        // ── Step 2: Unlike a plain Employee (who has no ".app-nav-menu" at all), Priya
        // gets a sidebar — but MainLayout.razor renders it with a single "Company" item
        // and none of the full HR menu groups (People, Assets, Recruitment, Leave, Dashboard).
        var navMenu = _page.Locator(".app-nav-menu");
        Assert.True(await navMenu.IsVisibleAsync(),
            "Priya (CompanyAdministrator-only) should see a sidebar nav menu containing the Company item");

        var navText = (await navMenu.TextContentAsync())?.Trim() ?? "";
        Assert.Contains("Company", navText);
        Assert.DoesNotContain("Dashboard", navText);
        Assert.DoesNotContain("People", navText);
        Assert.DoesNotContain("Assets", navText);
        Assert.DoesNotContain("Recruitment", navText);
        Assert.DoesNotContain("Leave", navText);
    }

    [Fact]
    public async Task CompanyAdministrator_CannotAccess_EmployeeList()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        // ── Step 1: Login as Priya (CompanyAdministrator-only) ────────────────
        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

        // ── Step 2: Attempt to navigate directly to the employee list route ────
        // Proves the employee:manage policy narrowing is enforced end-to-end (backend),
        // not merely hidden from the sidebar UI.
        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/employees");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15_000 });

        // ── Step 3: Must be redirected away from the bare employee list route ──
        var finalUrl = _page.Url;
        Assert.False(finalUrl.TrimEnd('/').EndsWith($"/companies/{AcmeId}/employees", StringComparison.OrdinalIgnoreCase),
            $"Expected Priya (CompanyAdministrator-only, no employee:manage) to be redirected away from the employee list page, but ended up at: {finalUrl}");
    }

    [Fact]
    public async Task CompanyAdministrator_IsAlsoAnEmployee_AndCanReachMyProfile()
    {
        // Priya Shah is a Company Administrator, but she's also a real employee (Chief Financial
        // Officer, Finance dept) — EmployeesModule seeds her with the same id as her
        // ApplicationUser so Session.EmployeeId resolves and the top-bar avatar link
        // (MainLayout.razor's ".top-bar-user", gated on Session.EmployeeId.HasValue) appears for
        // her just like it does for any other employee.
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

        await _page.WaitForURLAsync(new Regex($"/companies/{AcmeId}/edit"), new() { Timeout = 15_000 });

        var avatarLink = _page.Locator("a.top-bar-user");
        Assert.True(await avatarLink.IsVisibleAsync(),
            "Expected the top-bar avatar link to My Profile to be visible for Priya (Company Administrator, but also an employee)");

        await avatarLink.ClickAsync();
        await profile.WaitForLoadAsync();

        Assert.Contains($"/employees/", _page.Url);
        Assert.Contains("/profile", _page.Url);
    }

    [Fact]
    public async Task CompanyAdministrator_CannotAccess_HrSettingsPage()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        // ── Step 1: Login as Priya (CompanyAdministrator-only) ────────────────
        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

        // ── Step 2: Attempt to navigate directly to the standalone HR Settings page ──
        // Priya has Session.CanManageCompany but not Session.IsHrAdministrator, which is what
        // HrSettingsPage.razor's LoadAsync gates on (redirecting to Session.MyProfileUrl
        // otherwise). Proves the HR-policy fields that moved off the Company Settings tab are
        // no longer reachable by a Company-Administrator-only persona, closing the permission
        // gap that used to let her edit them via CompanySettingsTab.
        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/hr-settings");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15_000 });

        var finalUrl = _page.Url;
        Assert.False(finalUrl.TrimEnd('/').EndsWith($"/companies/{AcmeId}/hr-settings", StringComparison.OrdinalIgnoreCase),
            $"Expected Priya (CompanyAdministrator-only, no IsHrAdministrator) to be redirected away " +
            $"from the HR Settings page, but ended up at: {finalUrl}");
    }

    [Fact]
    public async Task HrAdministrator_StillSeesFullSidebar_AndDashboard()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        // ── Step 1: Login as Laura (HrAdministrator — CanManageEmployees true) ─
        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        // ── Step 2: Full HR sidebar is unaffected by the Company-Administrator-only
        // narrowing — she still sees the full menu, including HR-only groups like "People".
        var navMenu = _page.Locator(".app-nav-menu");
        Assert.True(await navMenu.IsVisibleAsync(),
            "Laura (HrAdministrator) should see the full admin navigation menu in the sidebar");

        var navText = (await navMenu.TextContentAsync())?.Trim() ?? "";
        Assert.Contains("People", navText);

        // ── Step 3: Home.razor redirects the full-HR persona to her role-specific dashboard
        // (see AppSession.LandingUrl) rather than to a Company edit URL.
        await _page.WaitForURLAsync(new Regex(@"/dashboard/hr"), new() { Timeout = 15_000 });
        Assert.Equal($"{_fixture.WebBaseUrl}/dashboard/hr".TrimEnd('/'), _page.Url.TrimEnd('/'));
    }
}
