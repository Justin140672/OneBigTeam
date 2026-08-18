using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies that a CompanyAdministrator-only user (CanManageCompany true, CanManageEmployees
/// false — no other HR role) is scoped to the Company edit screen only:
/// - Landing on "/" redirects straight to the Company edit page (Home.razor).
/// - No sidebar is shown at all (MainLayout.razor's ShowSidebar deliberately excludes
///   CanManageCompany — her whole job is company profile/settings, reachable directly from her
///   landing page, so a full nav menu is unnecessary surface area), same as a plain Employee.
/// - Backend access to the employee list is still denied even via direct navigation, proving
///   the narrowing isn't just a hidden UI affordance.
///
/// Contrasted against an HrAdministrator (CanManageEmployees true), who should still see the
/// full sidebar and land on her role-specific dashboard ("/dashboard/hr") rather than the
/// Company edit screen — unaffected by this change.
/// </summary>
public sealed class CompanyAdministratorAccessTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
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
    public async Task CompanyAdministrator_SeesNoSidebar()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        // ── Step 1: Login as Priya (CompanyAdministrator-only) ────────────────
        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

        // ── Step 2: Same as a plain Employee, Priya gets no sidebar at all —
        // MainLayout.razor's ShowSidebar deliberately excludes CanManageCompany (her whole job
        // is company profile/settings, reachable directly from her landing page — see
        // CompanyAdministrator_RedirectedFromRoot_ToCompanyEdit above), so there's no ".app-nav-menu"
        // to show a "Company" item (or a User Administration item — HR-Administrator-only, see
        // IdentityModule.AddRolePolicies' "users:view"/"users:manage" policies) in.
        await _page.WaitForURLAsync(new Regex($"/companies/{AcmeId}/edit"), new() { Timeout = 15_000 });

        Assert.False(await _page.Locator(".app-nav-menu").IsVisibleAsync(),
            "Priya (CompanyAdministrator-only) should not see a sidebar nav menu");
    }

    [Fact]
    public async Task CompanyAdministrator_CannotAccess_UserAdministration()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        // ── Step 1: Login as Priya (CompanyAdministrator-only) ────────────────
        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

        // ── Step 2: Attempt to navigate directly to the User Administration list ──
        // Proves the users:view/users:manage policy narrowing to HrAdministrator-only is
        // enforced end-to-end (backend + UserAdministrationList.razor's own OnBeforeLoadAsync
        // redirect), not merely hidden from the sidebar UI — same pattern as
        // CompanyAdministrator_CannotAccess_EmployeeList/HrSettingsPage above.
        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/user-administration");
        // See E2ETestBase.WaitForUrlToStopContainingAsync's doc comment: the redirect is a
        // client-side Blazor NavigateTo, not a full page navigation, so NetworkIdle after the
        // initial GET is not a reliable signal that the redirect has completed.
        await WaitForUrlToStopContainingAsync("/user-administration");

        var finalUrl = _page.Url;
        Assert.False(finalUrl.TrimEnd('/').EndsWith($"/companies/{AcmeId}/user-administration", StringComparison.OrdinalIgnoreCase),
            $"Expected Priya (CompanyAdministrator-only, no IsHrAdministrator) to be redirected away " +
            $"from the User Administration page, but ended up at: {finalUrl}");
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
        // The redirect is a client-side Blazor NavigateTo (OnBeforeLoadAsync), not a full page
        // navigation, so NetworkIdle after the initial GET is not a reliable completion signal
        // (same reasoning as E2ETestBase.WaitForUrlToStopContainingAsync). That shared helper's
        // plain substring check doesn't work here though: the redirect target
        // (Session.MyProfileUrl) is itself "/companies/{id}/employees/{empId}/profile", which
        // still *contains* the bare "/companies/{id}/employees" route as a prefix, so polling for
        // that substring to disappear would never resolve early. Poll for the URL no longer being
        // exactly the bare list route instead.
        var bareListUrl = $"{_fixture.WebBaseUrl}/companies/{AcmeId}/employees".TrimEnd('/');
        await _page.WaitForURLAsync(url => url.TrimEnd('/') != bareListUrl, new() { Timeout = 15_000 });

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

        // MyProfilePage.WaitForLoadAsync only waits for ".e-tab" to exist, which the Company Edit
        // page (Profile/Settings tabs) we're navigating away FROM also has — so it can resolve
        // against tabs still left over from the pre-navigation page without proving the click
        // actually navigated anywhere yet, and Url could still read the stale /companies/{id}/edit
        // address a beat after the click. Wait for the URL to actually land on the profile route
        // first, so a slow/failed navigation surfaces as a clear timeout here rather than a
        // confusing "wrong URL" assertion failure below.
        await _page.WaitForURLAsync(new Regex(@"/employees/[0-9a-fA-F-]{36}/profile"), new() { Timeout = 15_000 });
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
        // IsVisibleAsync() (unlike Playwright's auto-retrying Expect assertions) checks the DOM
        // once with no wait/retry — right after LoginAsync returns, the post-login redirect to
        // the role-specific dashboard (and the sidebar it renders) may not have completed yet, so
        // asserting immediately can race the render. Wait for the element to actually appear first.
        var navMenu = _page.Locator(".app-nav-menu");
        await navMenu.WaitForAsync(new() { Timeout = 15_000 });
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
