using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the Recruitment Kanban board (VacancyKanbanBoard.razor / KanbanCandidateCard.razor,
/// tickets #69-#73, reworked to dynamic per-company stages by tickets #97-#101).
///
/// The board now renders one column per RecruitmentStage the applying company has configured, in
/// DisplayOrder, rather than a fixed 8-status enum layout — so this class asserts against the actual
/// seeded stage set for Acme (RecruitmentStageSeeder.BuildDefaultStages: "Application Received",
/// "CV Review", "Interview", "Offer", "Hired", "Rejected", at DisplayOrder 1-6, "Hired"/"Rejected"
/// being the only terminal stages) instead of a hardcoded enum column list/count. The seeder only
/// runs the first time recruitment data exists for a company (on first Vacancy creation), which is
/// guaranteed here since ArrangeAppliedApplicationAsync always creates a fresh vacancy first.
///
/// Also: the old client-side transition graph (ApplicationStatusTransitionRules) that used to
/// pre-block "invalid" drags before ever calling the server was deleted as part of #99/#101 — stage
/// order is now arbitrary per company and the server (MoveApplicationStageHandler) only rejects a
/// move when the application is currently on a terminal stage, or the target stage is inactive/not
/// found. There is no remaining E2E coverage for the "target stage is inactive" rejection path:
/// GetRecruitmentKanbanHandler filters its Columns to IsActive stages only, so deactivating a stage
/// removes its column from the board entirely rather than leaving it there-but-rejecting — there is
/// no way to drag onto an inactive stage through the UI anymore to exercise that server-side check.
///
/// Uses the seeded Acme company (00000000-0000-0000-0000-000000000001) and Marcus Diallo
/// (Recruiter role) throughout, the same persona used by ApplicationToEmployeeFlowTests and
/// RecruitmentDashboardTests. Each test creates its own fresh Candidate/Vacancy/Application (unique
/// names per run) via the Applications tab's "Add Candidate" flow, rather than reusing the seeded
/// data, so runs don't collide with each other or with other test classes sharing this database.
/// </summary>
public sealed class VacancyKanbanBoardTests(RecruiterPersonaFixture fixture) : RoleE2ETestBase<RecruiterPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string MarcusEmail = "marcus.diallo@acme.example";
    private const string LauraEmail = "laura.bennett@acme.example";

    private const string InitialStage      = "Application Received";
    private const string NonTerminalStage2 = "CV Review";
    private const string TerminalHired     = "Hired";
    private static readonly string[] AllSeededStages =
    [
        "Application Received", "CV Review", "Interview", "Offer", "Hired", "Rejected",
    ];

    [Fact]
    public async Task Board_RendersOneColumnPerConfiguredStage_InDisplayOrder_IncludingEmptyColumns()
    {
        var (candidateLast, kanban) = await ArrangeAppliedApplicationAsync();

        foreach (var stage in AllSeededStages)
        {
            Assert.True(await kanban.HasColumnHeaderAsync(stage),
                $"Expected a Kanban column header for stage '{stage}' to render, including stages with no current candidates");
        }

        Assert.True(await kanban.GetColumnCountAsync(InitialStage) >= 1,
            $"Expected the '{InitialStage}' column count to include the new application for {candidateLast}");

        Assert.Equal(0, await kanban.GetColumnCountAsync(TerminalHired));
    }

    [Fact]
    public async Task SearchBox_FiltersVisibleCards_ByCandidateName()
    {
        var (candidateLast, kanban) = await ArrangeAppliedApplicationAsync();

        Assert.True(await kanban.HasCardForNameAsync(candidateLast),
            "Expected the new candidate's card to be visible before filtering");

        await kanban.FillSearchAsync("NoSuchCandidateXyz");
        Assert.False(await kanban.HasCardForNameAsync(candidateLast),
            "Expected the candidate's card to be hidden once the search term no longer matches their name");
        Assert.Equal(0, await kanban.CountVisibleCardsAsync());

        await kanban.FillSearchAsync(candidateLast);
        Assert.True(await kanban.HasCardForNameAsync(candidateLast),
            "Expected the candidate's card to reappear once the search term matches their name again");
    }

    [Fact]
    public async Task ClickingCard_NavigatesToTheUnderlyingCandidate()
    {
        var (candidateLast, kanban) = await ArrangeAppliedApplicationAsync();

        await kanban.ClickCardAsync(candidateLast);

        await _page.WaitForURLAsync(new System.Text.RegularExpressions.Regex(@"/candidates/[0-9a-f-]{36}"),
            new() { Timeout = 15_000 });
        Assert.Matches(@"/candidates/[0-9a-f-]{36}", _page.Url);

        var candidateEdit = new CandidateEditPage(_page, _fixture.WebBaseUrl);
        Assert.Equal("E2E", await candidateEdit.GetFirstNameAsync());
    }

    [Fact]
    public async Task ClickingCard_InCvReviewStage_OpensReviewCvWithReturnUrl()
    {
        var (candidateLast, kanban) = await ArrangeAppliedApplicationAsync();

        await kanban.DragCardToColumnAsync(candidateLast, NonTerminalStage2);
        Assert.True(await kanban.IsCardInColumnAsync(candidateLast, NonTerminalStage2),
            $"Expected the card for {candidateLast} to be in the '{NonTerminalStage2}' column before clicking it");

        await kanban.ClickCardAsync(candidateLast);

        await _page.WaitForURLAsync(new System.Text.RegularExpressions.Regex(@"/applications/[0-9a-f-]{36}/review-cv\?returnUrl="),
            new() { Timeout = 15_000 });
        Assert.DoesNotMatch(@"/candidates/[0-9a-f-]{36}", _page.Url);
    }

    [Fact]
    public async Task ClickingCard_OutsideCvReviewStage_DoesNotOpenReviewCv()
    {
        var (candidateLast, kanban) = await ArrangeAppliedApplicationAsync();

        await kanban.ClickCardAsync(candidateLast);

        await _page.WaitForURLAsync(new System.Text.RegularExpressions.Regex(@"/candidates/[0-9a-f-]{36}"),
            new() { Timeout = 15_000 });
        Assert.DoesNotContain("/review-cv", _page.Url);
    }

    [Fact]
    public async Task DraggingCard_MovesApplicationToTargetStage_AndPersistsAcrossReload()
    {
        var (candidateLast, kanban) = await ArrangeAppliedApplicationAsync();

        Assert.True(await kanban.IsCardInColumnAsync(candidateLast, InitialStage),
            $"Expected the freshly created application to start on '{InitialStage}'");
        Assert.False(await kanban.IsCardInColumnAsync(candidateLast, NonTerminalStage2),
            $"Sanity check: the card should not already be on '{NonTerminalStage2}' before dragging");

        await kanban.DragCardToColumnAsync(candidateLast, NonTerminalStage2);

        Assert.True(await kanban.IsCardInColumnAsync(candidateLast, NonTerminalStage2),
            $"Expected the card for {candidateLast} to appear in the '{NonTerminalStage2}' column after the drag");
        Assert.False(await kanban.IsCardInColumnAsync(candidateLast, InitialStage),
            $"Expected the card for {candidateLast} to no longer be in the '{InitialStage}' column after the drag");

        var vacancyId = ExtractVacancyIdFromUrl(_page.Url);
        var reloadedKanban = new VacancyKanbanBoardPage(_page, _fixture.WebBaseUrl);
        await reloadedKanban.GoToStandaloneAsync(AcmeId, vacancyId);

        Assert.True(await reloadedKanban.IsCardInColumnAsync(candidateLast, NonTerminalStage2),
            $"Expected the move to '{NonTerminalStage2}' to have persisted server-side after a fresh page load");
        Assert.False(await reloadedKanban.IsCardInColumnAsync(candidateLast, InitialStage),
            $"Expected the card to no longer be reported on '{InitialStage}' after a fresh page load");
    }

    private static Guid ExtractVacancyIdFromUrl(string url)
    {
        var match = System.Text.RegularExpressions.Regex.Match(url, @"/vacancies/([0-9a-fA-F-]{36})");
        if (!match.Success)
            throw new InvalidOperationException($"Could not extract a vacancy id from URL '{url}'.");
        return Guid.Parse(match.Groups[1].Value);
    }

    [Fact]
    public async Task WithdrawnApplication_IsNotShownOnTheBoard()
    {
        var (candidateLast, kanban) = await ArrangeAppliedApplicationAsync();

        Assert.True(await kanban.HasCardForNameAsync(candidateLast),
            "Expected the freshly created application's card to be visible before withdrawal");

        var vacancyId = ExtractVacancyIdFromUrl(_page.Url);

        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);
        await vacancyDetail.GoToAsync(AcmeId, vacancyId);
        await vacancyDetail.OpenApplicationsTabAsync();
        await vacancyDetail.ClickWithdrawForAsync(candidateLast);

        await kanban.GoToStandaloneAsync(AcmeId, vacancyId);

        Assert.False(await kanban.HasCardForNameAsync(candidateLast),
            "Expected the withdrawn application's card to no longer appear anywhere on the board");
        Assert.False(await kanban.IsCardInColumnAsync(candidateLast, InitialStage),
            $"Expected the withdrawn application to no longer be reported under '{InitialStage}' either");
    }

    [Fact]
    public async Task RecruitmentDashboard_TogglingBetweenBoardAndList_RemembersBoardSearchFilter()
    {
        var (candidateLast, _) = await ArrangeAppliedApplicationAsync();

        var dashboard = new RecruitmentDashboardPage(_page, _fixture.WebBaseUrl);
        await dashboard.GoToAsync();

        var vacancyPicker = _page.Locator(".recruitment-dashboard-vacancy-picker");
        await vacancyPicker.WaitForAsync(new() { Timeout = 15_000 });
        await DropDownSelector.SelectAsync(_page, vacancyPicker, _vacancyTitle!);

        var kanban = new VacancyKanbanBoardPage(_page, _fixture.WebBaseUrl);
        await kanban.WaitForLoadedAsync();

        await kanban.FillSearchAsync(candidateLast);
        Assert.True(await kanban.HasCardForNameAsync(candidateLast));

        await _page.Locator("[data-testid='recruitment-view-list-btn']").ClickAsync();
        Assert.True(await _page.Locator("[data-testid='recruitment-view-toggle']").IsVisibleAsync());

        await _page.Locator("[data-testid='recruitment-view-board-btn']").ClickAsync();
        await kanban.WaitForLoadedAsync();

        await Assertions.Expect(_page.Locator("input[aria-label='Search pipeline candidates']").First)
            .ToHaveValueAsync(candidateLast, new() { Timeout = 15_000 });
        await kanban.WaitForCardPresentAsync(candidateLast);
    }

    private string? _vacancyTitle;

    private async Task<(string CandidateLast, VacancyKanbanBoardPage Kanban)> ArrangeAppliedApplicationAsync()
    {
        var unique         = Guid.NewGuid().ToString("N")[..8];
        var candidateFirst = "E2E";
        var candidateLast  = $"Kanban{unique}";
        var candidateName = $"{candidateFirst} {candidateLast}";
        var candidateEmail = $"e2e.kanban{unique}@example.com";
        var vacancyTitle   = $"E2E Kanban Role {unique}";
        _vacancyTitle = vacancyTitle;

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

        // A fresh Position Profile is required here rather than the seeded "Senior Software
        // Engineer" — that profile already has a permanently-open vacancy in seed data (see
        // PositionProfileTestHelpers' remarks), which the "one live vacancy per position profile"
        // rule would otherwise reject a second vacancy against.
        var profileTitle = await PositionProfileTestHelpers.CreateUniquePositionProfileAsync(
            _page, _fixture.WebBaseUrl, AcmeId, login, LauraEmail, MarcusEmail);

        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickNewVacancyAsync();
        await vacancyDetail.FillTitleAsync(vacancyTitle);
        await vacancyDetail.SelectPositionProfileAsync(profileTitle);
        await vacancyDetail.SelectHiringManagerAsync("James");
        await vacancyDetail.SelectEmploymentTypeAsync("Permanent");
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

        return (candidateLast, kanban);
    }
}
