using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the My Tasks dashboard widget.
///
/// Uses Sarah Chen (sarah.chen@acme.example) who has seeded open tasks
/// (acknowledgement task a0000000-…-0020, return task a0000000-…-0022).
/// </summary>
[Collection("E2E")]
public sealed class MyTasksWidgetTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private const string SarahEmail = "sarah.chen@acme.example";

    [Fact]
    public async Task ViewAll_NavigatesToProfile_WithTasksTabActive()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new DashboardPage(_page, _fixture.WebBaseUrl);
        var profile   = new MyProfilePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(SarahEmail);
        await dashboard.GoToAsync();

        await dashboard.ClickViewAllTasksAsync();
        await profile.WaitForLoadAsync();

        var activeTab = await profile.GetActiveTabNameAsync();
        Assert.Equal("Tasks", activeTab);
    }

    [Fact]
    public async Task ViewAll_UrlContainsTabTasks()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new DashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(SarahEmail);
        await dashboard.GoToAsync();

        await dashboard.ClickViewAllTasksAsync();

        Assert.Contains("tab=tasks", _page.Url);
    }
}
