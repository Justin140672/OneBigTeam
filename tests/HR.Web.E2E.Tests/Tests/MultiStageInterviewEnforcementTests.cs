using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

public sealed class MultiStageInterviewEnforcementTests(RecruiterPersonaFixture fixture) : RoleE2ETestBase<RecruiterPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string MarcusEmail = "marcus.diallo@acme.example";
    private const string LauraEmail = "laura.bennett@acme.example";

    private const string InitialStage = "Application Received";
    private const string OfferStage = "Offer";
    private const string RejectedStage = "Rejected";

    [Fact]
    public async Task ApplicationsTab_Offer_Is_Disabled_And_Schedule_Is_Enabled_Before_Any_Interview()
    {
        var (candidateLast, vacancyDetail, _) = await ArrangeAppliedApplicationAsync();

        Assert.True(await vacancyDetail.IsApplicationsToolbarButtonEnabledAsync(candidateLast, "Schedule Interview"));
        Assert.False(await vacancyDetail.IsApplicationsToolbarButtonEnabledAsync(candidateLast, "Offer", exact: true));
    }

    [Fact]
    public async Task Kanban_Generic_Move_Directly_To_Offer_Is_Rejected_With_Error()
    {
        var (candidateLast, _, vacancyId) = await ArrangeAppliedApplicationAsync();

        var kanban = new VacancyKanbanBoardPage(_page, _fixture.WebBaseUrl);
        await kanban.GoToStandaloneAsync(AcmeId, vacancyId);

        await kanban.MoveToStageViaMouseAsync(candidateLast, OfferStage);

        Assert.True(await kanban.IsErrorVisibleAsync(), "Expected the server's stage-move validation error to be shown");
        Assert.True(await kanban.IsCardInColumnAsync(candidateLast, InitialStage));
    }

    [Fact]
    public async Task Kanban_Drag_Directly_To_Offer_Is_Rejected_And_Card_Stays_Put()
    {
        var (candidateLast, _, vacancyId) = await ArrangeAppliedApplicationAsync();

        var kanban = new VacancyKanbanBoardPage(_page, _fixture.WebBaseUrl);
        await kanban.GoToStandaloneAsync(AcmeId, vacancyId);

        await kanban.DragCardToColumnAsync(candidateLast, OfferStage);

        Assert.True(await kanban.IsErrorVisibleAsync());
        Assert.True(await kanban.IsCardInColumnAsync(candidateLast, InitialStage));
    }

    [Fact]
    public async Task Offer_Becomes_Available_On_Applications_Tab_And_Kanban_Only_After_Interview_Passed()
    {
        var (candidateLast, vacancyDetail, vacancyId) = await ArrangeAppliedApplicationAsync();

        await ScheduleInterviewAsync(vacancyDetail, candidateLast);
        await vacancyDetail.OpenApplicationsTabAsync();
        Assert.False(await vacancyDetail.IsApplicationsToolbarButtonEnabledAsync(candidateLast, "Offer", exact: true));
        Assert.False(await vacancyDetail.IsApplicationsToolbarButtonEnabledAsync(candidateLast, "Schedule Interview"));
        Assert.True(await vacancyDetail.IsApplicationsToolbarButtonEnabledAsync(candidateLast, "Record Outcome"));

        await RecordOutcomeAsync(vacancyDetail, candidateLast, "Passed");
        await vacancyDetail.OpenApplicationsTabAsync();
        Assert.True(await vacancyDetail.IsApplicationsToolbarButtonEnabledAsync(candidateLast, "Offer", exact: true));

        var kanban = new VacancyKanbanBoardPage(_page, _fixture.WebBaseUrl);
        await kanban.GoToStandaloneAsync(AcmeId, vacancyId);
        await _page.Locator(".kanban-candidate-card").Filter(new() { HasText = candidateLast }).First
            .Locator("[data-testid='kanban-card-move-stage-btn']").ClickAsync();
        await _page.Locator("[data-testid='kanban-card-make-offer']").WaitForAsync(new() { Timeout = 10_000 });
    }

    [Fact]
    public async Task Rejecting_A_Candidate_With_A_Pending_Interview_Moves_Them_To_Rejected()
    {
        var (candidateLast, vacancyDetail, vacancyId) = await ArrangeAppliedApplicationAsync();

        await ScheduleInterviewAsync(vacancyDetail, candidateLast);
        await vacancyDetail.OpenApplicationsTabAsync();
        Assert.True(await vacancyDetail.IsApplicationsToolbarButtonEnabledAsync(candidateLast, "Reject"));

        await vacancyDetail.ClickRejectForAsync(candidateLast);
        await vacancyDetail.SubmitRejectAsync();

        var kanban = new VacancyKanbanBoardPage(_page, _fixture.WebBaseUrl);
        await kanban.GoToStandaloneAsync(AcmeId, vacancyId);
        Assert.True(await kanban.IsCardInColumnAsync(candidateLast, RejectedStage));
    }

    private async Task ScheduleInterviewAsync(VacancyDetailPage vacancyDetail, string candidateLast)
    {
        await vacancyDetail.ClickScheduleInterviewForAsync(candidateLast);
        await vacancyDetail.WaitForScheduleDialogAsync();
        await vacancyDetail.SelectInterviewerAsync("James");
        await vacancyDetail.FillScheduledAtAsync("01/09/2099 10:00");
        await vacancyDetail.SubmitScheduleInterviewAsync();
    }

    private static async Task RecordOutcomeAsync(VacancyDetailPage vacancyDetail, string candidateLast, string outcome)
    {
        await vacancyDetail.OpenInterviewsTabAsync();
        await vacancyDetail.ClickRecordOutcomeForAsync(candidateLast);
        await vacancyDetail.WaitForOutcomeDialogAsync();
        await vacancyDetail.SelectOutcomeAsync(outcome);
        await vacancyDetail.SubmitOutcomeAsync();
    }

    private static Guid ExtractVacancyIdFromUrl(string url)
    {
        var match = Regex.Match(url, @"/vacancies/([0-9a-fA-F-]{36})");
        if (!match.Success)
            throw new InvalidOperationException($"Could not extract a vacancy id from URL '{url}'.");
        return Guid.Parse(match.Groups[1].Value);
    }

    private async Task<(string CandidateLast, VacancyDetailPage VacancyDetail, Guid VacancyId)> ArrangeAppliedApplicationAsync()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var candidateFirst = "E2E";
        var candidateLast = $"MultiStage{unique}";
        var candidateName = $"{candidateFirst} {candidateLast}";
        var candidateEmail = $"e2e.multistage{unique}@example.com";
        var vacancyTitle = $"E2E Multi Stage Role {unique}";

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var candidateList = new CandidateListPage(_page, _fixture.WebBaseUrl);
        var candidateEdit = new CandidateEditPage(_page, _fixture.WebBaseUrl);
        var vacancyList = new VacancyListPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await candidateList.GoToAsync(AcmeId);
        await candidateList.ClickNewCandidateAsync();
        await candidateEdit.FillFirstNameAsync(candidateFirst);
        await candidateEdit.FillLastNameAsync(candidateLast);
        await candidateEdit.FillEmailAsync(candidateEmail);
        await candidateEdit.SaveNewCandidateAsync();

        var profileTitle = await PositionProfileTestHelpers.CreateUniquePositionProfileAsync(
            _page, _fixture.WebBaseUrl, AcmeId, login, LauraEmail, MarcusEmail);

        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickNewVacancyAsync();
        await vacancyDetail.FillTitleAsync(vacancyTitle);
        await vacancyDetail.SelectPositionProfileAsync(profileTitle);
        await vacancyDetail.SelectHiringManagerAsync("James");
        await vacancyDetail.SaveNewVacancyAsync();

        await vacancyList.ClickVacancyAsync(vacancyTitle);
        await vacancyDetail.PublishVacancyAsync();
        await vacancyDetail.OpenApplicationsTabAsync();
        await vacancyDetail.ClickAddCandidateAsync();
        await vacancyDetail.SelectCandidateInAddDialogAsync(candidateName);
        await vacancyDetail.SubmitAddApplicationAsync();

        Assert.Equal(InitialStage, await vacancyDetail.GetApplicationStatusAsync(candidateLast));

        return (candidateLast, vacancyDetail, ExtractVacancyIdFromUrl(_page.Url));
    }
}
