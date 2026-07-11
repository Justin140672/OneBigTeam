using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the probation review task detail screen.
///
/// Uses the seeded "Complete probation review — Carlos Rivera" task
/// (ID: a0000000-0000-0000-0000-000000000005), which is a ManagerCheckIn review
/// assigned to Sarah Chen.
/// </summary>
[Collection("E2E")]
public sealed class ProbationReviewTaskTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId  = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid SarahId = Guid.Parse("30000000-0000-0000-0000-000000000001");

    // Seeded probation review task — ManagerCheckIn for Carlos Rivera, assigned to Sarah.
    private static readonly Guid TaskProbationReviewId = Guid.Parse("a0000000-0000-0000-0000-000000000005");

    // A non-probation-review task used to verify the panel is absent for other sources.
    // Originally Sarah's TaskSource.Manual "Review Q2 performance reports" task
    // (a0000000-...0001); that source has been removed entirely, so this now points at her
    // existing seeded Asset-acknowledgement task instead — it's an unrelated, real-domain
    // task and serves the same purpose (verifying the probation panel doesn't render for it).
    private static readonly Guid TaskQ2ReviewId = Guid.Parse("a0000000-0000-0000-0000-000000000021");

    private const string SarahEmail = "sarah.chen@acme.example";

    [Fact]
    public async Task TaskView_ShowsProbationReviewPanel_ForProbationReviewTask()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var taskView = new TaskViewPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(SarahEmail);

        await taskView.GoToAsync(AcmeId, SarahId, TaskProbationReviewId);

        Assert.True(await taskView.HasProbationReviewPanelAsync(),
            "Expected 'Complete Probation Review' panel to be visible for a ProbationReview task");
    }

    [Fact]
    public async Task TaskView_ShowsCorrectReviewType_ForManagerCheckIn()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var taskView = new TaskViewPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(SarahEmail);

        await taskView.GoToAsync(AcmeId, SarahId, TaskProbationReviewId);

        var reviewType = await taskView.GetProbationReviewTypeAsync();
        Assert.Equal("Manager Check-in", reviewType);
    }

    [Fact]
    public async Task TaskView_ShowsTaskTitle_ForProbationReviewTask()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var taskView = new TaskViewPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(SarahEmail);

        await taskView.GoToAsync(AcmeId, SarahId, TaskProbationReviewId);

        var title = await taskView.GetTitleAsync();
        Assert.Contains("Carlos Rivera", title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TaskView_NoProbationReviewPanel_ForNonProbationTask()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var taskView = new TaskViewPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(SarahEmail);

        await taskView.GoToAsync(AcmeId, SarahId, TaskQ2ReviewId);

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
        await login.LoginAsync(SarahEmail);

        await taskView.GoToAsync(AcmeId, SarahId, TaskProbationReviewId);

        var statusBefore = await taskView.GetStatusAsync();
        Assert.NotEqual("Completed", statusBefore);

        await taskView.EnterReviewNotesAsync("Performance is satisfactory. Recommending pass.");
        await taskView.CompleteReviewAsync();

        Assert.Equal("Completed", await taskView.GetStatusAsync());
    }
}
