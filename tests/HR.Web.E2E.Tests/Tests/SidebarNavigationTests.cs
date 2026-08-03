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
/// 2. A Manager-only persona also now gets a sidebar, with a "Manager Dashboard" link
///    ("/dashboard/manager") plus "Reporting".
/// 3. MainLayout.razor's ShowSidebar includes IsManager and IsRecruiter on their own (this used
///    to deliberately exclude them — a Manager/Recruiter-only persona's dashboard was considered
///    their home with no separate admin section to reach — but both roles are "reporting:view"-
///    entitled server-side, so they need a way to reach the sidebar-only Reporting nav item; see
///    ShowSidebar's own comment). The "Manager Dashboard"/"Recruitment Dashboard" labelled links
///    (as opposed to the plain "Dashboard" label) only appear for a user who holds an *additional*
///    role beyond Manager/Recruiter alone (e.g. an HR Administrator who is also a Manager).
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
    public async Task ManagerOnly_SeesSidebar_WithManagerDashboardLink()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var sidebar = new SidebarPage(_page);

        await login.GoToAsync();
        await login.LoginAsync(JamesEmail);

        // Home.razor's post-login redirect lands James on /dashboard/manager.
        await _page.WaitForURLAsync(new Regex("/dashboard/manager"), new() { Timeout = 15_000 });
        Assert.Contains("/dashboard/manager", _page.Url);

        // MainLayout.razor's ShowSidebar includes IsManager on its own — a Manager-only persona
        // sees a sidebar with a "Manager Dashboard" link (plus "Reporting").
        Assert.True(await sidebar.IsSidebarVisibleAsync(),
            "Expected a Manager-only persona to see a sidebar");
        Assert.True(await sidebar.HasTopLevelMenuItemAsync("Manager Dashboard"));
        Assert.True(await sidebar.HasTopLevelMenuItemAsync("Reporting"));
    }
}
