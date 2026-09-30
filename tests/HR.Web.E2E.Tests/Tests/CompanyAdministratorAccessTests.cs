using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class CompanyAdministratorAccessTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string CompanyAdminEmail = "priya.shah@acme.example";

    private const string HrAdminEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task CompanyAdministrator_RedirectedFromRoot_ToCompanyEdit()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

        await _page.WaitForURLAsync(new Regex($"/companies/{AcmeId}/edit"), new() { Timeout = 15_000 });

        Assert.Contains($"/companies/{AcmeId}/edit", _page.Url);
    }

    [Fact]
    public async Task CompanyAdministrator_SeesSidebar_WithOnlyTheCompanyAdministrationGroup()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var sidebar = new SidebarPage(_page);

        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

        await _page.WaitForURLAsync(new Regex($"/companies/{AcmeId}/edit"), new() { Timeout = 15_000 });

        Assert.True(await sidebar.IsSidebarVisibleAsync(),
            "A CompanyAdministrator needs the sidebar to reach Subscription & Billing / Company Profile");

        Assert.True(await sidebar.HasGroupedMenuItemAsync("Company administration", "Subscription & Billing"),
            "Expected the CompanyAdministrator's sidebar to contain the Company administration group");
        Assert.False(await sidebar.HasTopLevelMenuItemAsync("People and users"),
            "A CompanyAdministrator-only user must not see the People and users nav group");
        Assert.False(await sidebar.HasTopLevelMenuItemAsync("HR configuration"),
            "A CompanyAdministrator-only user must not see the HR configuration nav group");
    }

    [Fact]
    public async Task CompanyAdministrator_CannotAccess_UserAdministration()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/user-administration");
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

        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/employees");
        var bareListUrl = $"{_fixture.WebBaseUrl}/companies/{AcmeId}/employees".TrimEnd('/');
        await _page.WaitForURLAsync(url => url.TrimEnd('/') != bareListUrl, new() { Timeout = 15_000 });

        var finalUrl = _page.Url;
        Assert.False(finalUrl.TrimEnd('/').EndsWith($"/companies/{AcmeId}/employees", StringComparison.OrdinalIgnoreCase),
            $"Expected Priya (CompanyAdministrator-only, no employee:manage) to be redirected away from the employee list page, but ended up at: {finalUrl}");
    }

    [Fact]
    public async Task CompanyAdministrator_IsAlsoAnEmployee_AndCanReachMyProfile()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

        await _page.WaitForURLAsync(new Regex($"/companies/{AcmeId}/edit"), new() { Timeout = 15_000 });

        var avatarLink = _page.Locator("a.top-bar-user");
        Assert.True(await avatarLink.IsVisibleAsync(),
            "Expected the top-bar avatar link to My Profile to be visible for Priya (Company Administrator, but also an employee)");

        await avatarLink.ClickAsync();

        await _page.WaitForURLAsync(new Regex(@"/employees/[0-9a-fA-F-]{36}/profile"), new() { Timeout = 15_000 });
        await profile.WaitForLoadAsync();

        Assert.Contains($"/employees/", _page.Url);
        Assert.Contains("/profile", _page.Url);
    }

    [Fact]
    public async Task CompanyAdministrator_CannotAccess_HrSettingsPage()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/hr-settings");
        await _page.WaitForURLAsync(url => !url.Split('?')[0].TrimEnd('/').EndsWith($"/companies/{AcmeId}/hr-settings", StringComparison.OrdinalIgnoreCase), new() { Timeout = 30_000 });

        var finalUrl = _page.Url;
        Assert.False(finalUrl.TrimEnd('/').EndsWith($"/companies/{AcmeId}/hr-settings", StringComparison.OrdinalIgnoreCase),
            $"Expected Priya (CompanyAdministrator-only, no IsHrAdministrator) to be redirected away " +
            $"from the HR Settings page, but ended up at: {finalUrl}");
    }

    [Fact]
    public async Task HrAdministrator_StillSeesFullSidebar_AndDashboard()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        var navMenu = _page.Locator(".app-nav-menu");
        await navMenu.WaitForAsync(new() { Timeout = 15_000 });
        Assert.True(await navMenu.IsVisibleAsync(),
            "Laura (HrAdministrator) should see the full admin navigation menu in the sidebar");

        var navText = (await navMenu.TextContentAsync())?.Trim() ?? "";
        Assert.Contains("People", navText);

        await _page.WaitForURLAsync(new Regex(@"/dashboard/hr"), new() { Timeout = 15_000 });
        Assert.Equal($"{_fixture.WebBaseUrl}/dashboard/hr".TrimEnd('/'), _page.Url.TrimEnd('/'));
    }
}
