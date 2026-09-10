using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the "Review CV" screen (ReviewCv.razor, ticket #1) and its two entry points — the vacancy
/// Kanban board applicant card menu (KanbanApplicantCard.razor's "Review CV" menu item) and the
/// per-row "Review CV" link on the Vacancy Detail Applications tab (VacancyApplicationsTab.razor).
///
/// Each test creates its own fresh Candidate + Position Profile + Vacancy + Application (unique names
/// per run) via the same flow as VacancyKanbanBoardTests.ArrangeAppliedApplicationAsync, leaving the
/// application on the seeded initial stage ("Application Received"), rather than reusing the shared
/// seeded Acme applications (Emma Clarke et al.) — Move Forward / Reject mutate stage, and doing that
/// to shared seed data would race RecruitmentDashboardTests / RecruitmentPipelineReportTests etc.
///
/// Uses Marcus Diallo (Recruiter role) — recruitment:manage is Recruiter-only. Serializes against
/// the CrossUserVacancyTestBase gate the same way VacancyKanbanBoardTests / RecruitmentStageManagement
/// Tests do: Move Forward resolves "the next active non-terminal stage" from Acme's shared, ordered
/// recruitment pipeline config, which RecruitmentStageManagementTests mutates.
/// </summary>
public sealed class CandidateCvReviewTests(RecruiterPersonaFixture fixture) : RoleE2ETestBase<RecruiterPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string MarcusEmail = "marcus.diallo@acme.example";
    private const string LauraEmail  = "laura.bennett@acme.example";

    // RecruitmentStageSeeder.BuildDefaultStages — a freshly created Application starts on the first
    // (DisplayOrder 1); Move Forward advances it to the next active non-terminal stage.
    private const string InitialStage = "Application Received";
    private const string NextStage    = "CV Review";
    private const string RejectedStage = "Rejected";

    public override async Task InitializeAsync()
    {
        await CrossUserVacancyTestBase.GateInstance.WaitAsync();
        await base.InitializeAsync();
    }

    public override async Task DisposeAsync()
    {
        try
        {
            await base.DisposeAsync();
        }
        finally
        {
            CrossUserVacancyTestBase.GateInstance.Release();
        }
    }

    [Fact]
    public async Task ReviewCv_FromKanbanCardMenu_LoadsWithCandidateVacancyAndStage()
    {
        var arranged = await ArrangeApplicationAsync();
        var review = await OpenReviewCvFromKanbanAsync(arranged);

        Assert.Contains(arranged.CandidateLast, await review.GetCandidateNameAsync() ?? "");
        Assert.Equal(arranged.VacancyTitle, await review.GetPositionAsync());
        Assert.Equal(InitialStage, await review.GetCurrentStageAsync());
    }

    [Fact]
    public async Task ReviewCv_FromApplicationsTabRowLink_LoadsPage()
    {
        var arranged = await ArrangeApplicationAsync();

        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);
        await vacancyDetail.GoToAsync(AcmeId, arranged.VacancyId);
        await vacancyDetail.OpenApplicationsTabAsync();
        await vacancyDetail.ClickReviewCvForAsync(arranged.CandidateLast);

        var review = new ReviewCvPage(_page, _fixture.WebBaseUrl);
        await review.WaitForLoadedAsync();

        Assert.Contains(arranged.CandidateLast, await review.GetCandidateNameAsync() ?? "");
        Assert.Equal(InitialStage, await review.GetCurrentStageAsync());
    }

    [Fact]
    public async Task SaveNotes_PersistsAcrossReload_StageUnchanged()
    {
        var arranged = await ArrangeApplicationAsync();
        var review = await OpenReviewCvFromKanbanAsync(arranged);

        var notes = $"Strong CV — relevant platform experience. {Guid.NewGuid():N}";
        await review.SetNotesAsync(notes);
        await review.SaveNotesAsync();
        Assert.True(await review.HasSuccessAlertAsync());

        var applicationId = review.GetApplicationIdFromUrl();

        // Fresh navigation → proves the notes were persisted server-side, not just left in local
        // component state.
        await review.GoToAsync(AcmeId, arranged.VacancyId, applicationId);
        Assert.Equal(notes, await review.GetNotesAsync());
        Assert.Equal(InitialStage, await review.GetCurrentStageAsync());

        // Saving notes must not have moved the applicant on the board either.
        await arranged.Kanban.GoToStandaloneAsync(AcmeId, arranged.VacancyId);
        Assert.True(await arranged.Kanban.IsCardInColumnAsync(arranged.CandidateLast, InitialStage));
    }

    [Fact]
    public async Task MoveForward_AdvancesToNextStage_AndPersistsNotesEnteredBeforehand()
    {
        var arranged = await ArrangeApplicationAsync();
        var review = await OpenReviewCvFromKanbanAsync(arranged);

        var notes = $"Progressing to CV Review. {Guid.NewGuid():N}";
        await review.SetNotesAsync(notes);

        var applicationId = review.GetApplicationIdFromUrl();

        await review.MoveForwardAsync();

        // Returned to the board (the returnUrl the card menu carried).
        await arranged.Kanban.WaitForLoadedAsync();
        Assert.True(await arranged.Kanban.IsCardInColumnAsync(arranged.CandidateLast, NextStage),
            $"Expected the applicant to have moved to '{NextStage}' after Move Forward");
        Assert.False(await arranged.Kanban.IsCardInColumnAsync(arranged.CandidateLast, InitialStage));

        // Notes typed before Move Forward are persisted as part of the same call.
        await review.GoToAsync(AcmeId, arranged.VacancyId, applicationId);
        Assert.Equal(notes, await review.GetNotesAsync());
        Assert.Equal(NextStage, await review.GetCurrentStageAsync());
    }

    [Fact]
    public async Task Reject_ViaDialog_MovesApplicantToRejectedStage()
    {
        var arranged = await ArrangeApplicationAsync();
        var review = await OpenReviewCvFromKanbanAsync(arranged);

        await review.RejectAsync("Not enough depth in distributed systems for this role.");

        await arranged.Kanban.WaitForLoadedAsync();
        Assert.True(await arranged.Kanban.IsCardInColumnAsync(arranged.CandidateLast, RejectedStage),
            "Expected the applicant to land in the Rejected stage via the existing rejection workflow");
        Assert.False(await arranged.Kanban.IsCardInColumnAsync(arranged.CandidateLast, InitialStage));
    }

    [Fact]
    public async Task Close_ReturnsToOrigin_WithoutChangingStageOrNotes()
    {
        var arranged = await ArrangeApplicationAsync();
        var review = await OpenReviewCvFromKanbanAsync(arranged);

        var applicationId = review.GetApplicationIdFromUrl();

        // Type notes but DON'T save — Close must discard them and not move the applicant.
        await review.SetNotesAsync("Draft thoughts that should never be saved.");
        await review.CloseAsync();

        Assert.Contains("/kanban", _page.Url);

        await arranged.Kanban.WaitForLoadedAsync();
        Assert.True(await arranged.Kanban.IsCardInColumnAsync(arranged.CandidateLast, InitialStage),
            "Expected Close to leave the applicant on the original stage");

        await review.GoToAsync(AcmeId, arranged.VacancyId, applicationId);
        Assert.Equal(string.Empty, await review.GetNotesAsync());
        Assert.Equal(InitialStage, await review.GetCurrentStageAsync());
    }

    // ── Arrange helpers ─────────────────────────────────────────────────────────

    private sealed record ArrangedApplication(
        string CandidateLast, string CandidateName, string VacancyTitle, Guid VacancyId, VacancyKanbanBoardPage Kanban);

    /// <summary>
    /// Mirrors VacancyKanbanBoardTests.ArrangeAppliedApplicationAsync: fresh candidate + position
    /// profile + published vacancy + application (left on <see cref="InitialStage"/>), browser left
    /// on the vacancy's standalone Kanban board.
    /// </summary>
    private async Task<ArrangedApplication> ArrangeApplicationAsync()
    {
        var unique         = Guid.NewGuid().ToString("N")[..8];
        var candidateFirst = "E2E";
        var candidateLast  = $"Cv{unique}";
        var candidateName  = $"{candidateFirst} {candidateLast}";
        var candidateEmail = $"e2e.cv{unique}@example.com";
        var vacancyTitle   = $"E2E CV Review Role {unique}";

        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var candidateList = new CandidateListPage(_page, _fixture.WebBaseUrl);
        var candidateEdit = new CandidateEditPage(_page, _fixture.WebBaseUrl);
        var vacancyList   = new VacancyListPage(_page, _fixture.WebBaseUrl);
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

        var vacancyId = ExtractVacancyIdFromUrl(_page.Url);
        var kanban = new VacancyKanbanBoardPage(_page, _fixture.WebBaseUrl);
        await kanban.GoToStandaloneAsync(AcmeId, vacancyId);

        return new ArrangedApplication(candidateLast, candidateName, vacancyTitle, vacancyId, kanban);
    }

    private async Task<ReviewCvPage> OpenReviewCvFromKanbanAsync(ArrangedApplication arranged)
    {
        Assert.True(await arranged.Kanban.IsCardInColumnAsync(arranged.CandidateLast, InitialStage),
            "Expected the freshly created application to start on the initial stage before opening Review CV");

        await arranged.Kanban.ClickReviewCvFromCardMenuAsync(arranged.CandidateLast);

        var review = new ReviewCvPage(_page, _fixture.WebBaseUrl);
        await review.WaitForLoadedAsync();
        return review;
    }

    private static Guid ExtractVacancyIdFromUrl(string url)
    {
        var match = System.Text.RegularExpressions.Regex.Match(url, @"/vacancies/([0-9a-fA-F-]{36})");
        if (!match.Success)
            throw new InvalidOperationException($"Could not extract a vacancy id from URL '{url}'.");
        return Guid.Parse(match.Groups[1].Value);
    }
}
