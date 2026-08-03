using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the Recruitment Kanban board (VacancyKanbanBoard.razor / KanbanApplicantCard.razor,
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
/// found. The invalid-move test below exercises the "target stage is inactive" case (the only one
/// reachable purely through the UI, since reaching a terminal stage via the board removes the
/// "move off it" case from being a normal drag scenario without leaving Applied) and asserts a real
/// request round-trip (error banner from server error text) rather than an instant client no-op.
///
/// Uses the seeded Acme company (00000000-0000-0000-0000-000000000001) and Marcus Diallo
/// (Recruiter role) throughout, the same persona used by ApplicationToEmployeeFlowTests and
/// RecruitmentDashboardTests. Each test creates its own fresh Candidate/Vacancy/Application (unique
/// names per run) via the Applications tab's "Add Candidate" flow, rather than reusing the seeded
/// data, so runs don't collide with each other or with other test classes sharing this database.
/// </summary>
[Collection("E2E")]
public sealed class VacancyKanbanBoardTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string MarcusEmail = "marcus.diallo@acme.example";

    // RecruitmentStageSeeder.BuildDefaultStages — the default stage set every company gets the first
    // time recruitment data exists for it. A freshly created Application always starts on the first
    // of these (DisplayOrder 1, "Application Received").
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
                $"Expected a Kanban column header for stage '{stage}' to render, including stages with no current applicants");
        }

        // The freshly created application sits in the seeded initial stage — its column should show
        // a count of at least 1 (other tests/data may also share this stage on other vacancies'
        // boards, but this board is scoped to a single vacancy, so ours is the only contributor here).
        Assert.True(await kanban.GetColumnCountAsync(InitialStage) >= 1,
            $"Expected the '{InitialStage}' column count to include the new application for {candidateLast}");

        // A stage nothing has reached yet on this vacancy's board (e.g. "Hired") should still
        // render its header with a count of 0, not be hidden entirely.
        Assert.Equal(0, await kanban.GetColumnCountAsync(TerminalHired));
    }

    [Fact]
    public async Task SearchBox_FiltersVisibleCards_ByApplicantName()
    {
        var (candidateLast, kanban) = await ArrangeAppliedApplicationAsync();

        Assert.True(await kanban.HasCardForNameAsync(candidateLast),
            "Expected the new applicant's card to be visible before filtering");

        // Filter down to a name fragment that only matches a candidate that does not exist —
        // the real applicant's card must disappear.
        await kanban.FillSearchAsync("NoSuchApplicantXyz");
        Assert.False(await kanban.HasCardForNameAsync(candidateLast),
            "Expected the applicant's card to be hidden once the search term no longer matches their name");
        Assert.Equal(0, await kanban.CountVisibleCardsAsync());

        // Filtering back to (part of) the real name brings the card back.
        await kanban.FillSearchAsync(candidateLast);
        Assert.True(await kanban.HasCardForNameAsync(candidateLast),
            "Expected the applicant's card to reappear once the search term matches their name again");
    }

    [Fact]
    public async Task ClickingCard_NavigatesToTheUnderlyingCandidate()
    {
        var (candidateLast, kanban) = await ArrangeAppliedApplicationAsync();

        await kanban.ClickCardAsync(candidateLast);

        // VacancyKanbanBoard.OpenApplicant navigates to /companies/{companyId}/candidates/{candidateId}.
        await _page.WaitForURLAsync(new System.Text.RegularExpressions.Regex(@"/candidates/[0-9a-f-]{36}"),
            new() { Timeout = 15_000 });
        Assert.Matches(@"/candidates/[0-9a-f-]{36}", _page.Url);

        // The candidate detail page that loads should be for this same candidate.
        var candidateEdit = new CandidateEditPage(_page, _fixture.WebBaseUrl);
        Assert.Equal("E2E", await candidateEdit.GetFirstNameAsync());
    }

    [Fact]
    public async Task DragCard_ToAnotherActiveNonTerminalStage_MovesTheApplicationAndPersistsAcrossReload()
    {
        var (candidateLast, kanban) = await ArrangeAppliedApplicationAsync();

        // Ticket #99 removed the compiled linear transition graph — a company's stages can be
        // reordered/inserted freely, so any active, non-terminal target stage is now a valid move.
        await kanban.DragCardToColumnAsync(candidateLast, NonTerminalStage2);

        Assert.Equal(NonTerminalStage2, await kanban.GetCardStatusBadgeTextAsync(candidateLast));

        // Reload the board from scratch (fresh navigation, not just re-reading the DOM) — the move
        // must have actually been persisted server-side (MoveApplicationStageAsync), not merely
        // reflected client-side by the drag itself.
        await kanban.WaitForLoadedAsync();
        Assert.Equal(NonTerminalStage2, await kanban.GetCardStatusBadgeTextAsync(candidateLast));
    }

    [Fact]
    public async Task DragCard_ToInactiveStage_IsRejectedByServer_CardStaysPut_AndShowsError()
    {
        var (candidateLast, kanban) = await ArrangeAppliedApplicationAsync();

        // Deactivate the "Interview" stage via the Recruitment Stages settings page first — with the
        // client-side transition graph gone, this is the only "invalid move" a plain drag on the
        // board can still reach; MoveApplicationStageHandler rejects a move whose target stage is
        // inactive. Requires a real request round-trip (no client-side pre-check exists anymore).
        var stageList = new RecruitmentStageListPage(_page, _fixture.WebBaseUrl);
        await stageList.GoToAsync(AcmeId);
        if (await stageList.IsActiveAsync("Interview"))
            await stageList.DeactivateAsync("Interview");

        try
        {
            await kanban.DragCardToColumnAsync(candidateLast, "Interview");

            Assert.True(await kanban.IsErrorVisibleAsync(),
                "Expected an error banner after attempting to move an applicant to an inactive stage");
            var errorText = await kanban.GetErrorTextAsync();
            Assert.Contains("Interview", errorText ?? "");

            // The card must still be in its original column, not the rejected target.
            Assert.Equal(InitialStage, await kanban.GetCardStatusBadgeTextAsync(candidateLast));
        }
        finally
        {
            // Restore shared seeded state for other tests/classes sharing this database.
            await stageList.GoToAsync(AcmeId);
            await stageList.ShowInactiveAsync();
            if (!await stageList.IsActiveAsync("Interview"))
                await stageList.ActivateAsync("Interview");
        }
    }

    [Fact]
    public async Task RecruitmentDashboard_TogglingBetweenBoardAndList_RemembersBoardSearchFilter()
    {
        var (candidateLast, _) = await ArrangeAppliedApplicationAsync();

        var dashboard = new RecruitmentDashboardPage(_page, _fixture.WebBaseUrl);
        await dashboard.GoToAsync();

        // The dashboard defaults to the Board view already (RecruitmentDashboard.razor's
        // PipelineView.Board), showing a per-vacancy Kanban board via a vacancy picker.
        var vacancyPicker = _page.Locator("[data-testid='recruitment-board-vacancy-picker']");
        await vacancyPicker.WaitForAsync(new() { Timeout = 15_000 });
        await DropDownSelector.SelectAsync(_page, vacancyPicker, _vacancyTitle!);

        var kanban = new VacancyKanbanBoardPage(_page, _fixture.WebBaseUrl);
        await kanban.WaitForLoadedAsync();

        await kanban.FillSearchAsync(candidateLast);
        Assert.True(await kanban.HasCardForNameAsync(candidateLast));

        // Switch to the List view...
        await _page.Locator("[data-testid='recruitment-view-list-btn']").ClickAsync();
        Assert.True(await _page.Locator("[data-testid='recruitment-view-toggle']").IsVisibleAsync());

        // ...then back to Board. The search text lives in the Dashboard component itself
        // (_boardSearchText, bound via @bind-SearchText), so it must still be applied to the
        // board without the user having to re-type it.
        await _page.Locator("[data-testid='recruitment-view-board-btn']").ClickAsync();
        await kanban.WaitForLoadedAsync();

        Assert.True(await kanban.HasCardForNameAsync(candidateLast),
            "Expected the Kanban board's search filter to still be applied after switching away to the List view and back");
    }

    /// <summary>
    /// Creates a fresh candidate and vacancy, adds the candidate's application (leaving it on the
    /// seeded initial stage), then opens the vacancy's Kanban tab. Returns the candidate's (unique)
    /// last name and the ready-to-use board page object.
    /// </summary>
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

        // Position Profile is mandatory for creation — "Senior Software Engineer" is seeded for Acme.
        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickNewVacancyAsync();
        await vacancyDetail.FillTitleAsync(vacancyTitle);
        await vacancyDetail.SelectPositionProfileAsync("Senior Software Engineer");
        await vacancyDetail.SelectHiringManagerAsync("James");
        await vacancyDetail.SaveNewVacancyAsync();

        await vacancyList.ClickVacancyAsync(vacancyTitle);
        await vacancyDetail.PublishVacancyAsync();
        await vacancyDetail.OpenApplicationsTabAsync();
        await vacancyDetail.ClickAddCandidateAsync();
        await vacancyDetail.SelectCandidateInAddDialogAsync(candidateName);
        await vacancyDetail.SubmitAddApplicationAsync();

        Assert.Equal(InitialStage, await vacancyDetail.GetApplicationStatusAsync(candidateLast));

        await vacancyDetail.OpenKanbanTabAsync();

        var kanban = new VacancyKanbanBoardPage(_page, _fixture.WebBaseUrl);
        await kanban.WaitForLoadedAsync();

        return (candidateLast, kanban);
    }
}
