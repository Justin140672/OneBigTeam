using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies sidebar changes in MainLayout.razor:
/// 1. "Vacancies" and "Candidates" are no longer sidebar items at all — they now live as
///    prominent action buttons on the Recruitment Dashboard (see RecruitmentDashboardTests),
///    reached directly since a Recruiter-only persona lands there on login with no sidebar.
/// 2. New top-level role-gated dashboard links: "Dashboard" (Session.IsHrAdministrator, points to
///    "/dashboard/hr"), "Manager Dashboard" (Session.IsManager, "/dashboard/manager") and
///    "Recruitment Dashboard" (Session.IsRecruiter, "/dashboard/recruitment"). Note that
///    MainLayout.razor's ShowSidebar deliberately excludes IsManager and IsRecruiter on their
///    own — a Manager-only or Recruiter-only persona's dashboard IS their home, with no separate
///    admin section to reach, so they get no sidebar at all (see
///    ManagerOnly_SeesNoSidebar_AndLandsDirectlyOnManagerDashboard and
///    RecruiterOnly_SeesNoSidebar_AndLandsDirectlyOnRecruitmentDashboard below). The "Manager
///    Dashboard"/"Recruitment Dashboard" links only ever appear for a user who *also* holds a
///    role that independently qualifies for the sidebar (e.g. an HR Administrator who is also a
///    Manager or Recruiter).
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
    public async Task RecruiterOnly_SeesNoSidebar_AndLandsDirectlyOnRecruitmentDashboard()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var sidebar = new SidebarPage(_page);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        // A Recruiter-only persona's dashboard IS their home — there's no separate admin section
        // to reach, so MainLayout.razor's ShowSidebar deliberately excludes IsRecruiter on its
        // own (it only renders the sidebar when combined with another qualifying role). Home.razor's
        // post-login redirect already lands Marcus on /dashboard/recruitment without him ever
        // needing to click anything, so there's nothing for a sidebar to do here.
        await _page.WaitForURLAsync(new Regex("/dashboard/recruitment"), new() { Timeout = 15_000 });
        Assert.Contains("/dashboard/recruitment", _page.Url);

        Assert.False(await sidebar.IsSidebarVisibleAsync(),
            "Expected a Recruiter-only persona (no other qualifying role) to see no sidebar at all");
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
