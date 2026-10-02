using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

internal static class AdminRoutes
{
    public static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public const string Employees          = "/employees";
    public const string UserAdministration = "/user-administration";
    public const string HrSettings         = "/hr-settings";
    public const string Reporting          = "/reporting";
    public const string Candidates         = "/candidates";
    public const string Vacancies          = "/vacancies";
    public const string SharedDocuments    = "/shared-documents";
    public const string LeavePolicies      = "/leave-policies";
    public const string CompanyEdit        = "/edit";
}

internal static class AdminAccessAssertions
{
    private static string Target(string webBaseUrl, string routeSuffix) =>
        $"{webBaseUrl}/companies/{AdminRoutes.AcmeId}{routeSuffix}".TrimEnd('/');

    private static string CurrentPath(IPage page) => page.Url.Split('?')[0].TrimEnd('/');

    public static async Task AssertDeniedAsync(IPage page, string webBaseUrl, string routeSuffix)
    {
        var target = Target(webBaseUrl, routeSuffix);
        await page.GotoAsync(target);

        try
        {
            await page.WaitForURLAsync(u => u.Split('?')[0].TrimEnd('/') != target, new() { Timeout = 25_000 });
        }
        catch (TimeoutException) { /* fall through to assert on the resulting URL */ }

        var final = CurrentPath(page);
        Assert.True(final != target,
            $"Expected this persona to be DENIED '{routeSuffix}' (redirect to /access-denied), but stayed on: {final}");
    }

    public static async Task AssertAllowedAsync(IPage page, string webBaseUrl, string routeSuffix)
    {
        var target = Target(webBaseUrl, routeSuffix);
        await page.GotoAsync(target);

        await Task.Delay(2_500);

        var final = CurrentPath(page);
        Assert.False(final.EndsWith(AccessDeniedPage.Route, StringComparison.OrdinalIgnoreCase),
            $"Expected this persona to be ALLOWED '{routeSuffix}', but was redirected to /access-denied");
        Assert.Equal(target, final);
    }
}

public sealed class AdministrativeRoleSeparationCompanyAdminTests(PriyaShahPersonaFixture fixture)
    : RoleE2ETestBase<PriyaShahPersonaFixture>(fixture)
{
    private const string Email = "priya.shah@acme.example";

    private async Task LoginAsync()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(Email);
    }

    [Theory]
    [InlineData(AdminRoutes.Employees)]
    [InlineData(AdminRoutes.UserAdministration)]
    [InlineData(AdminRoutes.HrSettings)]
    [InlineData(AdminRoutes.Reporting)]
    [InlineData(AdminRoutes.Candidates)]
    [InlineData(AdminRoutes.Vacancies)]
    [InlineData(AdminRoutes.SharedDocuments)]
    [InlineData(AdminRoutes.LeavePolicies)]
    public async Task CompanyAdminOnly_IsDenied(string route)
    {
        await LoginAsync();
        await AdminAccessAssertions.AssertDeniedAsync(_page, _fixture.WebBaseUrl, route);
    }

    [Fact]
    public async Task CompanyAdminOnly_IsAllowed_CompanyEdit()
    {
        await LoginAsync();
        await AdminAccessAssertions.AssertAllowedAsync(_page, _fixture.WebBaseUrl, AdminRoutes.CompanyEdit);
    }

    [Fact]
    public async Task CompanyAdminOnly_Sidebar_ShowsOnlyCompanyAdministration()
    {
        var sidebar = new SidebarPage(_page);

        await LoginAsync();
        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AdminRoutes.AcmeId}/edit");
        await _page.WaitForSelectorAsync(".app-shell", new() { Timeout = 15_000 });

        Assert.True(await sidebar.IsSidebarVisibleAsync());
        Assert.True(await sidebar.HasGroupedMenuItemAsync("Company administration", "Subscription & Billing"));
        Assert.False(await sidebar.HasTopLevelMenuItemAsync("People and users"));
        Assert.False(await sidebar.HasTopLevelMenuItemAsync("HR configuration"));
    }

    [Fact]
    public async Task DeniedRoute_LandsOnAccessDenied_WithWorkingHomeLink()
    {
        await LoginAsync();

        var accessDenied = new AccessDeniedPage(_page, _fixture.WebBaseUrl);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AdminRoutes.AcmeId}{AdminRoutes.Employees}");
        await accessDenied.WaitForLoadedAsync();

        Assert.True(accessDenied.IsOnRoute, $"Expected to land on /access-denied, was: {_page.Url}");
        Assert.True(await accessDenied.Heading.IsVisibleAsync(), "Expected the 'Access denied' heading to be visible");

        await accessDenied.GoHomeLink.ClickAsync();
        await _page.WaitForURLAsync(u => !u.Split('?')[0].TrimEnd('/').EndsWith(AccessDeniedPage.Route), new() { Timeout = 15_000 });

        Assert.False(accessDenied.IsOnRoute, "Expected the 'Go to home' link to navigate away from /access-denied");
    }
}

public sealed class AdministrativeRoleSeparationHrAdminTests(HrAdminPersonaFixture fixture)
    : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private const string Email = "laura.bennett@acme.example";

    private async Task LoginAsync()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(Email);
    }

    [Theory]
    [InlineData(AdminRoutes.Employees)]
    [InlineData(AdminRoutes.UserAdministration)]
    [InlineData(AdminRoutes.HrSettings)]
    [InlineData(AdminRoutes.Reporting)]
    [InlineData(AdminRoutes.SharedDocuments)]
    [InlineData(AdminRoutes.LeavePolicies)]
    public async Task HrAdmin_IsAllowed(string route)
    {
        await LoginAsync();
        await AdminAccessAssertions.AssertAllowedAsync(_page, _fixture.WebBaseUrl, route);
    }

    [Theory]
    [InlineData(AdminRoutes.Candidates)]
    [InlineData(AdminRoutes.Vacancies)]
    [InlineData(AdminRoutes.CompanyEdit)]
    public async Task HrAdmin_IsDenied(string route)
    {
        await LoginAsync();
        await AdminAccessAssertions.AssertDeniedAsync(_page, _fixture.WebBaseUrl, route);
    }

    [Fact]
    public async Task HrAdmin_SeesSidebar_WithPeople_And_UserAdministration()
    {
        await LoginAsync();

        var navMenu = _page.Locator(".app-nav-menu");
        await navMenu.WaitForAsync(new() { Timeout = 15_000 });
        Assert.True(await navMenu.IsVisibleAsync(), "HR Administrator should see the admin navigation menu");

        var navText = (await navMenu.TextContentAsync())?.Trim() ?? "";
        Assert.Contains("People", navText);

        await navMenu.GetByText("People and users", new() { Exact = true }).ClickAsync();

        var userAdministrationLink = _page.GetByText("User Administration", new() { Exact = true });
        await userAdministrationLink.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        Assert.True(await userAdministrationLink.IsVisibleAsync(),
            "Expected a 'User Administration' nav item to be visible to an HR Administrator after expanding People and users");
    }
}

public sealed class AdministrativeRoleSeparationRecruiterTests(RecruiterPersonaFixture fixture)
    : RoleE2ETestBase<RecruiterPersonaFixture>(fixture)
{
    private const string Email = "marcus.diallo@acme.example";

    private async Task LoginAsync()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(Email);
    }

    [Theory]
    [InlineData(AdminRoutes.Candidates)]
    [InlineData(AdminRoutes.Vacancies)]
    [InlineData(AdminRoutes.Reporting)]
    public async Task Recruiter_IsAllowed(string route)
    {
        await LoginAsync();
        await AdminAccessAssertions.AssertAllowedAsync(_page, _fixture.WebBaseUrl, route);
    }

    [Theory]
    [InlineData(AdminRoutes.Employees)]
    [InlineData(AdminRoutes.UserAdministration)]
    [InlineData(AdminRoutes.HrSettings)]
    [InlineData(AdminRoutes.LeavePolicies)]
    [InlineData(AdminRoutes.CompanyEdit)]
    public async Task Recruiter_IsDenied(string route)
    {
        await LoginAsync();
        await AdminAccessAssertions.AssertDeniedAsync(_page, _fixture.WebBaseUrl, route);
    }

    [Fact]
    public async Task Recruiter_SeesSidebar_Without_UserAdministration_Or_HrSettings()
    {
        await LoginAsync();

        var navMenu = _page.Locator(".app-nav-menu");
        await navMenu.WaitForAsync(new() { Timeout = 15_000 });
        Assert.True(await navMenu.IsVisibleAsync(), "Recruiter should see the admin navigation menu");

        var navText = (await navMenu.TextContentAsync())?.Trim() ?? "";
        Assert.DoesNotContain("User Administration", navText);
        Assert.DoesNotContain("HR Settings", navText);
    }
}

public sealed class AdministrativeRoleSeparationManagerTests(ManagerPersonaFixture fixture)
    : RoleE2ETestBase<ManagerPersonaFixture>(fixture)
{
    private const string Email = "james.okafor@acme.example";

    private async Task LoginAsync()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(Email);
    }

    [Theory]
    [InlineData(AdminRoutes.Reporting)]
    public async Task Manager_IsAllowed(string route)
    {
        await LoginAsync();
        await AdminAccessAssertions.AssertAllowedAsync(_page, _fixture.WebBaseUrl, route);
    }

    [Theory]
    [InlineData(AdminRoutes.Employees)]
    [InlineData(AdminRoutes.UserAdministration)]
    [InlineData(AdminRoutes.HrSettings)]
    [InlineData(AdminRoutes.Candidates)]
    [InlineData(AdminRoutes.SharedDocuments)]
    [InlineData(AdminRoutes.LeavePolicies)]
    [InlineData(AdminRoutes.CompanyEdit)]
    public async Task Manager_IsDenied(string route)
    {
        await LoginAsync();
        await AdminAccessAssertions.AssertDeniedAsync(_page, _fixture.WebBaseUrl, route);
    }
}

public sealed class AdministrativeRoleSeparationEmployeeTests(EmployeePersonaFixture fixture)
    : RoleE2ETestBase<EmployeePersonaFixture>(fixture)
{
    private const string Email = "tom.williams@acme.example";

    private async Task LoginAsync()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(Email);
    }

    [Theory]
    [InlineData(AdminRoutes.Employees)]
    [InlineData(AdminRoutes.Reporting)]
    [InlineData(AdminRoutes.UserAdministration)]
    [InlineData(AdminRoutes.HrSettings)]
    [InlineData(AdminRoutes.Candidates)]
    [InlineData(AdminRoutes.Vacancies)]
    [InlineData(AdminRoutes.SharedDocuments)]
    [InlineData(AdminRoutes.LeavePolicies)]
    [InlineData(AdminRoutes.CompanyEdit)]
    public async Task PlainEmployee_IsDenied(string route)
    {
        await LoginAsync();
        await AdminAccessAssertions.AssertDeniedAsync(_page, _fixture.WebBaseUrl, route);
    }
}

public sealed class AdministrativeRoleSeparationCompanyAdminPlusManagerTests(SarahChenPersonaFixture fixture)
    : RoleE2ETestBase<SarahChenPersonaFixture>(fixture)
{
    private const string Email = "sarah.chen@acme.example";

    private async Task LoginAsync()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(Email);
    }

    [Theory]
    [InlineData(AdminRoutes.CompanyEdit)]
    [InlineData(AdminRoutes.Reporting)]
    public async Task CompanyAdminPlusManager_IsAllowed(string route)
    {
        await LoginAsync();
        await AdminAccessAssertions.AssertAllowedAsync(_page, _fixture.WebBaseUrl, route);
    }

    [Theory]
    [InlineData(AdminRoutes.Employees)]
    [InlineData(AdminRoutes.UserAdministration)]
    [InlineData(AdminRoutes.HrSettings)]
    public async Task CompanyAdminPlusManager_IsDenied(string route)
    {
        await LoginAsync();
        await AdminAccessAssertions.AssertDeniedAsync(_page, _fixture.WebBaseUrl, route);
    }
}
