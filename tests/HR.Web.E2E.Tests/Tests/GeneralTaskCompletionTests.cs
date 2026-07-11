using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies that an employee can view and complete a general (non-leave) task,
/// and that the status changes to "Completed" after the action.
///
/// Uses Sarah Chen's seeded task "Review Q2 performance reports"
/// (ID: a0000000-0000-0000-0000-000000000001), accessed directly via her own profile Tasks
/// tab (a self-service route unaffected by the "/" dashboard redirect that now applies to her
/// CompanyAdministrator + Manager role, which still lacks EmployeeEdit — see Home.razor).
///
/// The dashboard-widget check (<see cref="Dashboard_ShowsGeneralTasksForEmployee"/>) uses Laura
/// Bennett instead, since Sarah is redirected away from "/" and can never reach it.
/// </summary>
[Collection("E2E")]
public sealed class GeneralTaskCompletionTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId  = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid SarahId = Guid.Parse("30000000-0000-0000-0000-000000000001");

    // Seeded task assigned to Sarah Chen.
    private static readonly Guid TaskQ2ReviewId = Guid.Parse("a0000000-0000-0000-0000-000000000001");

    private const string SarahEmail = "sarah.chen@acme.example";
    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task TaskView_ShowsCorrectDetailsForGeneralTask()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var taskView = new TaskViewPage(_page, _fixture.WebBaseUrl);

        // ── Step 1: Login as Sarah ────────────────────────────────────────────
        await login.GoToAsync();
        await login.LoginAsync(SarahEmail);

        // ── Step 2: Navigate directly to the seeded task ──────────────────────
        await taskView.GoToAsync(AcmeId, SarahId, TaskQ2ReviewId);

        // ── Step 3: Verify title ──────────────────────────────────────────────
        var title = await taskView.GetTitleAsync();
        Assert.Contains("Q2", title, StringComparison.OrdinalIgnoreCase);

        // ── Step 4: This is NOT a leave task — the review panel must be absent ─
        Assert.False(await taskView.HasLeaveReviewPanelAsync(),
            "Expected no 'Review Leave Request' panel on a general (non-leave) task");

        // ── Step 5: Status should be "Not Started" ────────────────────────────
        var status = await taskView.GetStatusAsync();
        Assert.Equal("Not Started", status);
    }

    [Fact]
    public async Task Dashboard_ShowsGeneralTasksForEmployee()
    {
        // Sarah Chen is seeded as CompanyAdministrator-only and is redirected away from "/"
        // (see Home.razor), so this dashboard-widget check uses Laura Bennett instead, who was
        // given a parallel seeded "Review Q2 performance reports" task
        // (a0000000-0000-0000-0000-000000000025) for this purpose.
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var dash  = new DashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await dash.GoToAsync();

        var taskTitles = await dash.GetTaskTitlesAsync();

        // Laura has several seeded tasks; at least one should appear on the dashboard.
        Assert.True(taskTitles.Count > 0,
            "Expected Laura to have tasks in the My Tasks dashboard widget");

        Assert.Contains(taskTitles, t =>
            t.Contains("Q2", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("board meeting", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("survey", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TaskView_CompleteTask_ChangesStatusToCompleted()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var taskView = new TaskViewPage(_page, _fixture.WebBaseUrl);

        // Use "Analyse employee satisfaction survey results" (a0000000-0000-0000-0000-000000000004)
        // — a separate seeded task so this test does not conflict with other tests that use Q2.
        var taskSurveyId = Guid.Parse("a0000000-0000-0000-0000-000000000004");

        await login.GoToAsync();
        await login.LoginAsync(SarahEmail);

        await taskView.GoToAsync(AcmeId, SarahId, taskSurveyId);

        // Verify it is open / not yet completed.
        var statusBefore = await taskView.GetStatusAsync();
        Assert.NotEqual("Completed", statusBefore);

        await taskView.CompleteGeneralTaskAsync();

        Assert.Equal("Completed", await taskView.GetStatusAsync());
    }
}
