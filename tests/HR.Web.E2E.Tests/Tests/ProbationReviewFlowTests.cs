using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class ProbationReviewFlowTests(CrossUserFixture fixture) : RoleE2ETestBase<CrossUserFixture>(fixture)
{
    private static readonly Guid AcmeId           = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid ReviewerId           = Guid.Parse("30000000-0000-0000-0000-000000000008");
    private static readonly Guid SophieLaurent     = Guid.Parse("30000000-0000-0000-0000-000000000007");
    private static readonly Guid ProbationTaskId   = Guid.Parse("a0000000-0000-0000-0000-000000000026");

    private const string ReviewerEmail = "david.park@acme.example";
    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task CompletingReviewTask_IsReflectedOnProbationTab()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var taskView = new TaskViewPage(_page, _fixture.WebBaseUrl);
        var empEdit  = new EmployeeEditPage(_page, _fixture.WebBaseUrl);


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


        await login.SwitchAccountAsync(LauraEmail);

        string? reviewStatus = null;
        for (var attempt = 0; attempt < 8 && reviewStatus != "Completed"; attempt++)
        {
            if (attempt > 0)
                await _page.WaitForTimeoutAsync(2_000);

            await empEdit.GoToAsync(AcmeId, SophieLaurent);
            await empEdit.OpenProbationTabAsync();
            reviewStatus = await empEdit.GetReviewStatusInGridAsync("Manager Check-in");
        }

        Assert.Equal("Completed", reviewStatus);
    }

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
