using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

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

        await _page.WaitForURLAsync(new Regex("/dashboard/recruitment"), new() { Timeout = 15_000 });
        Assert.Contains("/dashboard/recruitment", _page.Url);

        Assert.True(await sidebar.IsSidebarVisibleAsync(),
            "Expected a Recruiter-only persona to see a sidebar");

        Assert.True(await sidebar.HasTopLevelMenuItemAsync("Dashboard"),
            "Expected a 'Dashboard' link (not 'Recruitment Dashboard') for a Recruiter-only persona");
        Assert.False(await sidebar.HasTopLevelMenuItemAsync("Recruitment Dashboard"),
            "Did not expect a link labelled exactly 'Recruitment Dashboard'");

        Assert.True(await sidebar.HasTopLevelMenuItemAsync("Vacancies"));
        Assert.True(await sidebar.HasTopLevelMenuItemAsync("Candidates"));
        Assert.True(await sidebar.HasTopLevelMenuItemAsync("Recruiters"));
        Assert.True(await sidebar.HasTopLevelMenuItemAsync("Recruitment Stages"));
        Assert.True(await sidebar.HasTopLevelMenuItemAsync("Reporting"));
        Assert.False(await sidebar.HasTopLevelMenuItemAsync("People and users"));
        Assert.False(await sidebar.HasTopLevelMenuItemAsync("Company"));
        Assert.False(await sidebar.HasTopLevelMenuItemAsync("HR configuration"));
    }

    [Fact]
    public async Task HrAdministrator_SeesDashboardLink_AndClickingItNavigatesToHrDashboard()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var sidebar = new SidebarPage(_page);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

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

        await _page.WaitForURLAsync(new Regex("/dashboard/manager"), new() { Timeout = 15_000 });
        Assert.Contains("/dashboard/manager", _page.Url);

        // MainLayout.razor's ShowSidebar deliberately excludes IsManager on its own — a
        // Manager-only persona's dashboard is their "home", with Reports reached via the
        // dashboard's own TeamReportsWidget rather than sidebar navigation (see ShowSidebar's
        // comment in MainLayout.razor).
        Assert.False(await sidebar.IsSidebarVisibleAsync(),
            "Expected a Manager-only persona to not see a sidebar");
    }

    [Fact]
    public async Task EmployeeOnly_DoesNotSeeSidebar()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var sidebar = new SidebarPage(_page);

        await login.GoToAsync();
        await login.LoginAsync("tom.williams@acme.example");

        Assert.False(await sidebar.IsSidebarVisibleAsync(),
            "Expected an Employee-only persona to not see a sidebar");
    }
}
