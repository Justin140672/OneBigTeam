using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies two sidebar changes in MainLayout.razor:
/// 1. The old "Recruitment" parent dropdown that used to wrap "Vacancies" and "Candidates" as
///    nested submenu items has been removed — both are now direct, top-level items shown
///    whenever Session.IsRecruiter, immediately visible/clickable with no parent to expand
///    first (unlike still-nested items such as "Organisation Chart" under "People" — see
///    OrganisationChartTests, which documents that Syncfusion's SfMenu doesn't render a nested
///    submenu's children into the DOM at all until the parent is clicked).
/// 2. New top-level role-gated dashboard links: "Dashboard" (Session.IsHrAdministrator, points to
///    "/dashboard/hr"), "Manager Dashboard" (Session.IsManager, "/dashboard/manager") and
///    "Recruitment Dashboard" (Session.IsRecruiter, "/dashboard/recruitment"). Note that
///    MainLayout.razor's ShowSidebar deliberately excludes IsManager on its own — a Manager-only
///    persona's dashboard IS their home, with no separate admin section to reach, so they get no
///    sidebar at all (see ManagerOnly_SeesNoSidebar_AndLandsDirectlyOnManagerDashboard below). The
///    "Manager Dashboard" link only ever appears for a Manager who *also* holds a role that
///    independently qualifies for the sidebar (e.g. an HR Administrator who is also a Manager).
///
/// Uses seeded personas:
///   - Marcus Diallo (marcus.diallo@acme.example) — Recruiter only (see RecruitmentDashboardTests).
///   - Laura Bennett (laura.bennett@acme.example) — HrAdministrator only (see HrDashboardTests).
///   - James Okafor (james.okafor@acme.example) — Manager only, not HrAdministrator or Recruiter
///     (see ManagerDashboardTests).
/// </summary>
[Collection("E2E")]
public sealed class SidebarNavigationTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private const string MarcusEmail = "marcus.diallo@acme.example";
    private const string LauraEmail  = "laura.bennett@acme.example";
    private const string JamesEmail  = "james.okafor@acme.example";

    [Fact]
    public async Task Recruiter_SeesVacanciesAndCandidates_AsDirectTopLevelItems_NotNestedUnderRecruitment()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var sidebar = new SidebarPage(_page);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        // No "Recruitment" parent item left at all — only "Recruitment Dashboard" remains, which
        // is a different, exact-text top-level item (see class remarks). HasTopLevelMenuItemAsync
        // asserts exact text, so this can't be confused with that item.
        Assert.False(await sidebar.HasTopLevelMenuItemAsync("Recruitment"),
            "Expected the old 'Recruitment' parent dropdown to no longer exist in the sidebar");

        // Both items are immediately visible without clicking any parent first — proving they
        // are top-level, not nested inside a (now-removed) "Recruitment" submenu.
        Assert.True(await sidebar.HasTopLevelMenuItemAsync("Vacancies"),
            "Expected 'Vacancies' to be a directly visible top-level sidebar item for a Recruiter");
        Assert.True(await sidebar.HasTopLevelMenuItemAsync("Candidates"),
            "Expected 'Candidates' to be a directly visible top-level sidebar item for a Recruiter");
    }

    [Fact]
    public async Task Recruiter_ClickingVacancies_NavigatesDirectly_WithoutExpandingAParent()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var sidebar = new SidebarPage(_page);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await sidebar.ClickTopLevelMenuItemAsync("Vacancies");

        await _page.WaitForURLAsync(new Regex("/vacancies"), new() { Timeout = 15_000 });
        Assert.Contains("/vacancies", _page.Url);
    }

    [Fact]
    public async Task Recruiter_ClickingCandidates_NavigatesDirectly_WithoutExpandingAParent()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var sidebar = new SidebarPage(_page);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await sidebar.ClickTopLevelMenuItemAsync("Candidates");

        await _page.WaitForURLAsync(new Regex("/candidates"), new() { Timeout = 15_000 });
        Assert.Contains("/candidates", _page.Url);
    }

    [Fact]
    public async Task HrAdministrator_SeesDashboardLink_AndClickingItNavigatesToHrDashboard()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var sidebar = new SidebarPage(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // Laura is HrAdministrator-only (not Manager or Recruiter), so "Dashboard" is the only
        // dashboard link she should see — asserted with exact text so this can't accidentally
        // match "Manager Dashboard"/"Recruitment Dashboard" for a differently-scoped persona.
        Assert.True(await sidebar.HasTopLevelMenuItemAsync("Dashboard"),
            "Expected an HR Administrator to see a 'Dashboard' link in the sidebar");

        await sidebar.ClickTopLevelMenuItemAsync("Dashboard");

        await _page.WaitForURLAsync(new Regex("/dashboard/hr"), new() { Timeout = 15_000 });
        Assert.Contains("/dashboard/hr", _page.Url);
    }

    [Fact]
    public async Task ManagerOnly_SeesNoSidebar_AndLandsDirectlyOnManagerDashboard()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var sidebar = new SidebarPage(_page);

        await login.GoToAsync();
        await login.LoginAsync(JamesEmail);

        // A Manager-only persona's dashboard IS their home — there's no separate admin section
        // to reach, so MainLayout.razor's ShowSidebar deliberately excludes IsManager on its own
        // (it only renders the sidebar when combined with another qualifying role). Home.razor's
        // post-login redirect already lands James on /dashboard/manager without him ever needing
        // to click anything, so there's nothing for a sidebar to do here.
        await _page.WaitForURLAsync(new Regex("/dashboard/manager"), new() { Timeout = 15_000 });
        Assert.Contains("/dashboard/manager", _page.Url);

        Assert.False(await sidebar.IsSidebarVisibleAsync(),
            "Expected a Manager-only persona (no other qualifying role) to see no sidebar at all");
    }
}
