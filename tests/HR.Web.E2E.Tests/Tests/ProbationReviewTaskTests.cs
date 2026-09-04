using HR.Modules.Tasks.Contracts;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the probation review task detail screen.
///
/// Uses the seeded "Complete probation review — Carlos Rivera" task
/// (ID: a0000000-0000-0000-0000-000000000005), a ManagerCheckIn review assigned to David Park —
/// Carlos's actual line manager and an HrAdministrator. The single-resource probation review
/// read (GET /probation-reviews/{id}) enforces reporting-chain / HR scope, so the task assignee
/// (and this test's persona) has to be someone who can genuinely view Carlos's review.
/// </summary>
public sealed class ProbationReviewTaskTests(DavidParkPersonaFixture fixture)
    : RoleE2ETestBase<DavidParkPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId  = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid DavidId = Guid.Parse("30000000-0000-0000-0000-000000000008");

    // Seeded probation review task — ManagerCheckIn for Carlos Rivera, assigned to David Park.
    private static readonly Guid TaskProbationReviewId = Guid.Parse("a0000000-0000-0000-0000-000000000005");

    // A non-probation-review task assigned to David Park, used to verify the probation panel is
    // absent for other sources (seeded generic TaskSource.Workflow task).
    private static readonly Guid TaskQ2ReviewId = Guid.Parse("a0000000-0000-0000-0000-00000000002a");

    private const string DavidEmail = "david.park@acme.example";

    [Fact]
    public async Task TaskView_ShowsProbationReviewPanel_ForProbationReviewTask()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var taskView = new TaskViewPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(DavidEmail);

        await taskView.GoToAsync(AcmeId, DavidId, TaskProbationReviewId);

        Assert.True(await taskView.HasProbationReviewPanelAsync(),
            "Expected 'Complete Probation Review' panel to be visible for a ProbationReview task");
    }

    [Fact]
    public async Task TaskView_ShowsCorrectReviewType_ForManagerCheckIn()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var taskView = new TaskViewPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(DavidEmail);

        await taskView.GoToAsync(AcmeId, DavidId, TaskProbationReviewId);

        var reviewType = await taskView.GetProbationReviewTypeAsync();
        Assert.Equal("Manager Check-in", reviewType);
    }

    [Fact]
    public async Task TaskView_ShowsTaskTitle_ForProbationReviewTask()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var taskView = new TaskViewPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(DavidEmail);

        await taskView.GoToAsync(AcmeId, DavidId, TaskProbationReviewId);

        var title = await taskView.GetTitleAsync();
        Assert.Contains("Carlos Rivera", title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TaskView_NoProbationReviewPanel_ForNonProbationTask()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var taskView = new TaskViewPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(DavidEmail);

        await taskView.GoToAsync(AcmeId, DavidId, TaskQ2ReviewId);

        Assert.False(await taskView.HasProbationReviewPanelAsync(),
            "Expected no 'Complete Probation Review' panel on a non-ProbationReview task");
    }

    // Run last — mutates the seeded task by completing the review.
    [Fact]
    public async Task TaskView_CompleteReview_ChangesStatusToCompleted()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var taskView = new TaskViewPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(DavidEmail);

        await taskView.GoToAsync(AcmeId, DavidId, TaskProbationReviewId);

        var statusBefore = await taskView.GetStatusAsync();
        Assert.NotEqual("Completed", statusBefore);

        await taskView.EnterReviewNotesAsync("Performance is satisfactory. Recommending pass.");
        await taskView.CompleteReviewAsync();

        Assert.Equal("Completed", await taskView.GetStatusAsync());
    }
}
