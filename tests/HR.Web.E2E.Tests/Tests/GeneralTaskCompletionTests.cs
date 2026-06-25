using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies that an employee can view and complete a general (non-leave) task,
/// and that the status changes to "Completed" after the action.
///
/// Uses Sarah Chen's seeded task "Review Q2 performance reports"
/// (ID: a0000000-0000-0000-0000-000000000001).
/// </summary>
[Collection("E2E")]
public sealed class GeneralTaskCompletionTests : IAsyncLifetime
{
    private static readonly Guid AcmeId  = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid SarahId = Guid.Parse("30000000-0000-0000-0000-000000000001");

    // Seeded task assigned to Sarah Chen.
    private static readonly Guid TaskQ2ReviewId = Guid.Parse("a0000000-0000-0000-0000-000000000001");

    private const string SarahEmail = "sarah.chen@acme.example";

    private readonly AppFixture _fixture;
    private IBrowserContext _context = null!;
    private IPage           _page    = null!;

    public GeneralTaskCompletionTests(AppFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _context = await _fixture.Browser.NewContextAsync();
        _page    = await _context.NewPageAsync();
    }

    public async Task DisposeAsync() => await _context.DisposeAsync();

    [Fact]
    public async Task TaskView_ShowsCorrectDetailsForGeneralTask()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var taskView = new TaskViewPage(_page, _fixture.WebBaseUrl);

        // ── Step 1: Login as Sarah ────────────────────────────────────────────
        await login.GoToAsync();
        await login.LoginAsync(SarahEmail);

        // ── Step 2: Navigate directly to the seeded task ──────────────────────
        await taskView.GoToAsync(AcmeId, TaskQ2ReviewId);

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
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var dash  = new DashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(SarahEmail);

        await dash.GoToAsync();

        var taskTitles = await dash.GetTaskTitlesAsync();

        // Sarah has several seeded tasks; at least one should appear on the dashboard.
        Assert.True(taskTitles.Count > 0,
            "Expected Sarah to have tasks in the My Tasks dashboard widget");

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

        await taskView.GoToAsync(AcmeId, taskSurveyId);

        // Verify it is open / not yet completed.
        var statusBefore = await taskView.GetStatusAsync();
        Assert.NotEqual("Completed", statusBefore);

        // Click the "Complete Task" or "Mark Complete" button.
        var completeBtn = _page.GetByRole(AriaRole.Button, new() { Name = "Complete" });
        if (!await completeBtn.IsVisibleAsync())
            completeBtn = _page.GetByRole(AriaRole.Button, new() { Name = "Mark Complete" });

        await completeBtn.ClickAsync();

        // Wait for status badge to update.
        await _page.WaitForFunctionAsync(
            "document.querySelector('.task-status-badge')?.textContent?.includes('Completed')",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });

        Assert.Equal("Completed", await taskView.GetStatusAsync());
    }
}
