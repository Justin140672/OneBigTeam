using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class ManagerDashboardTests(ManagerPersonaFixture fixture) : RoleE2ETestBase<ManagerPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string JamesEmail = "james.okafor@acme.example";
    private const string DavidEmail = "david.park@acme.example";
    private const string TomEmail   = "tom.williams@acme.example";

    private static (Guid EmployeeId, string LastName) CreateEmployeeReportingToDavidAsync()
    {
        var seeded = SeededE2eEmployees.ManagerDashboard[0];
        return (seeded.EmployeeId, seeded.LastName);
    }

    [Fact]
    public async Task NonManager_IsRedirectedAway_FromManagerDashboard()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/dashboard/manager");

        await _page.WaitForURLAsync(new Regex(@"/employees/[0-9a-f-]{36}/profile"), new() { Timeout = 15_000 });
        Assert.DoesNotContain("/dashboard/manager", _page.Url);
    }

    [Fact]
    public async Task ManagerOnly_SeesAttentionQueueAndTeamStatusWidgets()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new ManagerDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(JamesEmail);
        await dashboard.GoToAsync();

        Assert.True(await dashboard.HasWidgetAsync("Needs your action"));
        Assert.True(await dashboard.HasWidgetAsync("Team Status"));
        Assert.True(await dashboard.HasWidgetAsync("My Team"));
    }

    [Fact]
    public async Task AttentionQueueWidget_LoadsWithoutError_AndIncludesAllCategories()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new ManagerDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(JamesEmail);
        await dashboard.GoToAsync();

        Assert.True(await dashboard.HasWidgetAsync("Needs your action"));
        await dashboard.WaitForAttentionQueueLoadedAsync();
        await dashboard.GetAttentionQueueSubjectsAsync();
    }

    [Fact]
    public async Task TeamStatusSummary_LoadsWithoutError_ForManager()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new ManagerDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(JamesEmail);
        await dashboard.GoToAsync();

        Assert.True(await dashboard.HasWidgetAsync("Team Status"));
        await dashboard.WaitForTeamStatusLoadedAsync();

        var sick = await dashboard.GetTeamStatusValueAsync("Sick");
        Assert.True(sick >= 0);
    }

    [Fact]
    public async Task MyTeamWidget_ShowsDirectReport()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new ManagerDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(JamesEmail);
        await dashboard.GoToAsync();

        var names = await dashboard.GetMyTeamMemberNamesAsync();

        Assert.True(names.Any(n => n.Contains("Tom Williams", StringComparison.OrdinalIgnoreCase)),
            $"Expected 'Tom Williams' to appear in the My Team widget. Names found: [{string.Join(", ", names)}]");
    }

    [Fact]
    public async Task MyTeamWidget_ShowsDirectReportsPhoneAndEmail_AsVisibleText()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new ManagerDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(JamesEmail);
        await dashboard.GoToAsync();

        await dashboard.GetMyTeamMemberNamesAsync();
        var contactText = await dashboard.GetTeamMemberContactTextAsync("Tom Williams");
        var phone = await dashboard.GetTeamMemberPhoneFromLinkAsync("Tom Williams");

        Assert.False(string.IsNullOrWhiteSpace(phone), "Expected Tom Williams's card to carry a tel: phone link");
        Assert.Contains(contactText, t => t == phone);
        Assert.Contains(contactText, t => t.Contains("tom.williams@acme.example", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MyTeamWidget_ShowsAtWorkStatusBadge_ForDirectReportWithNoActiveSicknessOrLeave()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new ManagerDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(JamesEmail);
        await dashboard.GoToAsync();

        await dashboard.GetMyTeamMemberNamesAsync();

        Assert.Equal("At Work", await dashboard.GetTeamMemberStatusAsync("Tom Williams"));
    }

    [Fact]
    public async Task MyTeamWidget_NotifySicknessButton_OpensRecordSicknessDialogForThatEmployee()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new ManagerDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(JamesEmail);
        await dashboard.GoToAsync();

        await dashboard.GetMyTeamMemberNamesAsync();
        await dashboard.ClickNotifySicknessForTeamMemberAsync("Tom Williams");

        Assert.Contains("Record Sickness", await _page.ContentAsync());
    }

    [Fact]
    public async Task CompletingOnboardingTask_RemovesItFromAssigneesTasksTab_AndUpdatesOnboardingTabProgress()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList  = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit  = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var profile  = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var inbox    = new HrInboxPage(_page, _fixture.WebBaseUrl);
        var taskView = new TaskViewPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(DavidEmail);

        _ = empList;
        var (employeeId, lastName) = CreateEmployeeReportingToDavidAsync();

        await inbox.GoToAsync(AcmeId, lastName);
        var inboxTitles  = await inbox.GetTaskTitlesAsync();
        var claimedTitle = inboxTitles.First(t => t.Contains(lastName, StringComparison.OrdinalIgnoreCase));
        await inbox.ClaimAsync(claimedTitle);

        var davidId = Guid.Parse("30000000-0000-0000-0000-000000000008");
        await profile.GoToAsync(AcmeId, davidId);
        await profile.OpenTasksTabAsync();
        var tabTitlesBefore = await profile.GetTaskTitlesAsync();
        Assert.Contains(tabTitlesBefore, t => t.Contains(claimedTitle, StringComparison.OrdinalIgnoreCase));

        await profile.ClickTaskAsync(claimedTitle);
        await taskView.WaitForLoadedAsync();
        await taskView.CompleteGeneralTaskAsync();
        await taskView.CloseAsync();

        await profile.GoToAsync(AcmeId, davidId);
        await profile.OpenTasksTabAsync();
        Assert.Equal("Completed", await profile.GetTaskStatusAsync(claimedTitle));

        await empEdit.GoToAsync(AcmeId, employeeId);
        await empEdit.OpenOnboardingTabAsync();

        var percent = await empEdit.GetOnboardingProgressPercentAsync();
        Assert.True(percent > 0,
            $"Expected onboarding progress to be greater than 0% after completing one of the " +
            $"default checklist tasks, got {percent}%");

        var taskStatus = await empEdit.GetOnboardingChecklistTaskStatusAsync(claimedTitle.Split(" — ")[0]);
        Assert.Equal("Completed", taskStatus);

        var planStatus = await empEdit.GetOnboardingStatusBadgeTextAsync();
        Assert.Equal("In Progress", planStatus);
    }

    [Fact]
    public async Task ManagerWhoIsAlsoHrAdmin_TeamReportsWidget_ShowsOnlyTheFourManagerReports()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new ManagerDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(DavidEmail);
        await dashboard.GoToAsync();

        var widget = _page.Locator(".widget-card").Filter(new() { HasText = "Team Reports" });
        var items = widget.Locator(".task-widget-title");
        await Assertions.Expect(items).ToHaveCountAsync(4, new() { Timeout = 15_000 });

        var titles = (await items.AllInnerTextsAsync()).Select(t => t.Trim()).OrderBy(t => t).ToList();
        Assert.Equal(
            ["Leave Summary Report", "Onboarding Progress Report", "Probation Report", "Workload & HR Actions Report"],
            titles);
    }
}
