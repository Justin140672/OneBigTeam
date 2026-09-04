using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// End-to-end smoke test verifying that completing a probation review via the task view
/// is reflected on the employee's probation tab.
///
/// Uses the seeded Sophie Laurent employee (ID: 30000000-0000-0000-0000-000000000007,
/// company: Acme 00000000-0000-0000-0000-000000000001) who has an active ManagerCheckIn
/// review linked to task a0000000-0000-0000-0000-000000000026. This is a separate,
/// independent review from the Carlos Rivera scenario used by ProbationReviewTaskTests,
/// so completing it here does not affect that test's read-only assertions.
///
/// This test is designed to be resilient: if another test has already completed the review,
/// this test simply verifies the completed state is visible on the probation tab.
/// </summary>
public sealed class ProbationReviewFlowTests(CrossUserFixture fixture) : CrossUserTenantAndMiscTestBase(fixture)
{
    private static readonly Guid AcmeId           = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid ReviewerId           = Guid.Parse("30000000-0000-0000-0000-000000000008");
    private static readonly Guid SophieLaurent     = Guid.Parse("30000000-0000-0000-0000-000000000007");
    private static readonly Guid ProbationTaskId   = Guid.Parse("a0000000-0000-0000-0000-000000000026");

    // The "Complete probation review — Sophie Laurent" task is assigned to David Park
    // (HrAdministrator) — Sophie is a department head with no line manager, and the probation
    // review read is reporting-chain / HR scoped, so the assignee/reviewer must be HR.
    private const string ReviewerEmail = "david.park@acme.example";
    private const string LauraEmail = "laura.bennett@acme.example";

    /// <summary>
    /// Full flow: task view → complete review → probation tab shows Completed.
    /// </summary>
    [Fact]
    public async Task CompletingReviewTask_IsReflectedOnProbationTab()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var taskView = new TaskViewPage(_page, _fixture.WebBaseUrl);
        var empEdit  = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        // ── Step 1: Log in as the review task assignee (David Park) and complete the review ──

        // Gate against HrDashboardTests.UpcomingProbationReviewsWidget_ShowsCarlosRivera, which
        // reads a capped "upcoming probation reviews" list that Sophie's still-pending review can
        // evict Carlos Rivera's from — see SharedProbationGate's remarks in
        // GroupSerializedTestBases.cs.
        await SharedProbationGate.Instance.WaitAsync();
        try
        {
            await login.GoToAsync();
            await login.LoginAsync(ReviewerEmail);

            await taskView.GoToAsync(AcmeId, ReviewerId, ProbationTaskId);

            var statusBefore = await taskView.GetStatusAsync();
            if (statusBefore != "Completed")
            {
                await taskView.EnterReviewNotesAsync(
                    "Manager check-in complete. Sophie is meeting all objectives.");
                await taskView.CompleteReviewAsync();
            }

            Assert.Equal("Completed", await taskView.GetStatusAsync());
        }
        finally
        {
            SharedProbationGate.Instance.Release();
        }

        // ── Step 2: Switch to Laura (HR admin) and check the probation tab ──

        await login.SwitchAccountAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, SophieLaurent);
        await empEdit.OpenProbationTabAsync();

        var reviewStatus = await empEdit.GetReviewStatusInGridAsync("Manager Check-in");

        Assert.Equal("Completed", reviewStatus);
    }

    /// <summary>
    /// Verifies the probation tab review grid is populated before any task completion
    /// by navigating directly to the tab without touching the task.
    /// </summary>
    [Fact]
    public async Task ProbationTab_ShowsReviewHistory_Independent_Of_Task_State()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, SophieLaurent);
        await empEdit.OpenProbationTabAsync();

        Assert.True(await empEdit.HasProbationReviewsGridAsync(),
            "Expected the review history grid to be visible on the Probation tab");

        var status = await empEdit.GetProbationStatusBadgeTextAsync();
        Assert.NotNull(status);
        Assert.NotEmpty(status);
    }
}
