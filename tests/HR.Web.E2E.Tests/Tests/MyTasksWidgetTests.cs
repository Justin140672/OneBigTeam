using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the My Tasks dashboard widget.
///
/// Uses Sarah Chen (sarah.chen@acme.example) who has seeded open tasks including:
///   - acknowledgement task a0000000-…-0021 (Dell monitor — not consumed by other E2E tests)
///   - return task a0000000-…-0022 (Dell monitor — completed by AssetReturnTaskTests)
/// Uses Tom Williams (tom.williams@acme.example) who has:
///   - acknowledgement task a0000000-…-0020 (MacBook Pro — completed by AssetAcknowledgementTaskTests)
/// </summary>
[Collection("E2E")]
public sealed class MyTasksWidgetTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private const string SarahEmail = "sarah.chen@acme.example";
    private const string TomEmail   = "tom.williams@acme.example";

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

    [Fact]
    public async Task Widget_Shows_Asset_Acknowledgement_Task_For_Sarah()
    {
        // Uses seeded task a0000000-…-0021, which is not completed by any other E2E test.
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new DashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(SarahEmail);
        await dashboard.GoToAsync();

        var titles = await dashboard.GetTaskTitlesAsync();
        Assert.Contains(titles, t => t.Contains("Acknowledge", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Widget_ClickingAssetTask_NavigatesToTaskViewPage()
    {
        // Uses seeded task a0000000-…-0021, which is not completed by any other E2E test.
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new DashboardPage(_page, _fixture.WebBaseUrl);
        var taskView  = new TaskViewPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(SarahEmail);
        await dashboard.GoToAsync();

        await dashboard.ClickTaskAsync("Acknowledge");

        Assert.True(await taskView.HasAssetAcknowledgementPanelAsync(),
            "Expected the asset acknowledgement panel on the task view page after clicking the widget item");
    }
}
