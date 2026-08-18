using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies sidebar changes in MainLayout.razor:
/// 1. A Recruiter-only persona now gets a full sidebar (not just their dashboard as "home") with
///    "Vacancies", "Candidates", "Recruiters" and "Recruitment Settings" as top-level items, plus
///    a plain "Dashboard" link (not "Recruitment Dashboard" — that label is reserved for a combo
///    persona, see below) pointing at "/dashboard/recruitment".
/// 2. A Manager-only persona still gets no sidebar at all — their dashboard is "home", and
///    Reports are reached via the Reports section on that dashboard (see ManagerDashboard.razor's
///    TeamReportsWidget), not via sidebar navigation.
/// 3. MainLayout.razor's ShowSidebar includes IsRecruiter on its own (a plain Recruiter still
///    gets a sidebar since their workflow — Vacancies, Candidates, etc. — has no dashboard-
///    embedded equivalent for those sections; see ShowSidebar's own comment). The "Recruitment
///    Dashboard" labelled link (as opposed to the plain "Dashboard" label) only appears for a
///    user who holds an *additional* role beyond Recruiter alone (e.g. an HR Administrator who
///    is also a Recruiter).
///
/// Uses seeded personas:
///   - Marcus Diallo (marcus.diallo@acme.example) — Recruiter only (see RecruitmentDashboardTests).
///   - Laura Bennett (laura.bennett@acme.example) — HrAdministrator only (see HrDashboardTests).
///   - James Okafor (james.okafor@acme.example) — Manager only, not HrAdministrator or Recruiter
///     (see ManagerDashboardTests).
/// </summary>
public sealed class SidebarNavigationTests(ParallelBlankPersonaFixture fixture)
    : RoleE2ETestBase<ParallelBlankPersonaFixture>(fixture)
{
    private const string MarcusEmail = "marcus.diallo@acme.example";
    private const string LauraEmail  = "laura.bennett@acme.example";
    private const string JamesEmail  = "james.okafor@acme.example";

    [Fact]
    public async Task RecruiterOnly_SeesSidebar_WithDashboardVacanciesCandidatesRecruitersAndSettings()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var sidebar = new SidebarPage(_page);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        // Home.razor's post-login redirect lands Marcus on /dashboard/recruitment.
        await _page.WaitForURLAsync(new Regex("/dashboard/recruitment"), new() { Timeout = 15_000 });
        Assert.Contains("/dashboard/recruitment", _page.Url);

        // MainLayout.razor's ShowSidebar now includes IsRecruiter on its own (not just combined
        // with another qualifying role) — a Recruiter-only persona sees a sidebar with a
        // "Dashboard" link (not "Recruitment Dashboard" — that label is reserved for an
        // HR-Administrator-and-Recruiter combo persona's separate link) plus Vacancies,
        // Candidates, Recruiters and Recruitment Settings.
        Assert.True(await sidebar.IsSidebarVisibleAsync(),
            "Expected a Recruiter-only persona to see a sidebar");

        Assert.True(await sidebar.HasTopLevelMenuItemAsync("Dashboard"),
            "Expected a 'Dashboard' link (not 'Recruitment Dashboard') for a Recruiter-only persona");
        Assert.False(await sidebar.HasTopLevelMenuItemAsync("Recruitment Dashboard"),
            "Did not expect a link labelled exactly 'Recruitment Dashboard'");

        Assert.True(await sidebar.HasTopLevelMenuItemAsync("Vacancies"));
        Assert.True(await sidebar.HasTopLevelMenuItemAsync("Candidates"));
        Assert.True(await sidebar.HasTopLevelMenuItemAsync("Recruiters"));
        Assert.True(await sidebar.HasTopLevelMenuItemAsync("Recruitment Settings"));
        Assert.True(await sidebar.HasTopLevelMenuItemAsync("Reporting"));
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
    public async Task ManagerOnly_DoesNotSeeSidebar()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var sidebar = new SidebarPage(_page);

        await login.GoToAsync();
        await login.LoginAsync(JamesEmail);

        // Home.razor's post-login redirect lands James on /dashboard/manager.
        await _page.WaitForURLAsync(new Regex("/dashboard/manager"), new() { Timeout = 15_000 });
        Assert.Contains("/dashboard/manager", _page.Url);

        // MainLayout.razor's ShowSidebar deliberately excludes IsManager on its own — a
        // Manager-only persona's dashboard is their "home", with Reports reached via the
        // dashboard's own TeamReportsWidget rather than sidebar navigation (see ShowSidebar's
        // comment in MainLayout.razor).
        Assert.False(await sidebar.IsSidebarVisibleAsync(),
            "Expected a Manager-only persona to not see a sidebar");
    }
}
