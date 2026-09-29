using HR.Modules.Tasks.Contracts;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

public sealed class GeneralTaskCompletionTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId  = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid SarahId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid LauraId = Guid.Parse("30000000-0000-0000-0000-000000000005");

    private static readonly Guid TaskQ2ReviewId = Guid.Parse("a0000000-0000-0000-0000-000000000027");

    private const string SarahEmail = "sarah.chen@acme.example";
    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task TaskView_ShowsCorrectDetailsForGeneralTask()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var taskView = new TaskViewPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(SarahEmail);

        await taskView.GoToAsync(AcmeId, SarahId, TaskQ2ReviewId);

        var title = await taskView.GetTitleAsync();
        Assert.Contains("Q2", title, StringComparison.OrdinalIgnoreCase);

        Assert.False(await taskView.HasLeaveReviewPanelAsync(),
            "Expected no 'Review Leave Request' panel on a general (non-leave) task");

        var status = await taskView.GetStatusAsync();
        Assert.Equal("Not Started", status);
    }

    [Fact]
    public async Task ProfileTasksTab_ShowsGeneralTasksForEmployee()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await profile.GoToAsync(AcmeId, LauraId);
        await profile.OpenTasksTabAsync();

        var taskTitles = await profile.GetTaskTitlesAsync();

        Assert.True(taskTitles.Count > 0,
            "Expected Laura to have tasks in her Tasks tab");

        Assert.Contains(taskTitles, t =>
            t.Contains("leave policy", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("acknowledge", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TaskView_CompleteTask_ChangesStatusToCompleted()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var taskView = new TaskViewPage(_page, _fixture.WebBaseUrl);

        var taskSurveyId = Guid.Parse("a0000000-0000-0000-0000-000000000028");

        await login.GoToAsync();
        await login.LoginAsync(SarahEmail);

        await taskView.GoToAsync(AcmeId, SarahId, taskSurveyId);

        var statusBefore = await taskView.GetStatusAsync();
        Assert.NotEqual("Completed", statusBefore);

        await taskView.CompleteGeneralTaskAsync();

        Assert.Equal("Completed", await taskView.GetListTaskStatusAsync(taskSurveyId));
    }
}
