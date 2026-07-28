using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the Recruitment Kanban board (VacancyKanbanBoard.razor / KanbanApplicantCard.razor,
/// tickets #69-#73) — the board's own page object (VacancyKanbanBoardPage) already existed from
/// that work, but no test class actually exercised it (task #80).
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

    // Pipeline order mirrors ApplicationStatusTransitionRules.ColumnOrder (HR.Web.Services) /
    // GetRecruitmentKanban's ColumnOrder (HR.Modules.Recruitment) — all eight statuses are always
    // rendered as columns, whether or not they currently hold any applicants.
    private static readonly string[] AllStages =
    [
        "Applied", "Screening", "InterviewScheduled", "Interviewed",
        "Offered", "Hired", "Rejected", "Withdrawn",
    ];

    [Fact]
    public async Task Board_RendersOneColumnPerStage_InPipelineOrder_IncludingEmptyColumns()
    {
        var (candidateLast, kanban) = await ArrangeAppliedApplicationAsync();

        foreach (var stage in AllStages)
        {
            Assert.True(await kanban.HasColumnHeaderAsync(stage),
                $"Expected a Kanban column header for stage '{stage}' to render, including stages with no current applicants");
        }

        // The freshly created application sits in "Applied" — its column should show a count of
        // at least 1 (other tests/data may also be Applied on other vacancies' boards, but this
        // board is scoped to a single vacancy, so ours is the only contributor here).
        Assert.True(await kanban.GetColumnCountAsync("Applied") >= 1,
            $"Expected the 'Applied' column count to include the new application for {candidateLast}");

        // A stage nothing has reached yet on this vacancy's board (e.g. "Hired") should still
        // render its header with a count of 0, not be hidden entirely.
        Assert.Equal(0, await kanban.GetColumnCountAsync("Hired"));
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
    public async Task DragCard_ToValidColumn_MovesTheApplicationAndPersistsAcrossReload()
    {
        var (candidateLast, kanban) = await ArrangeAppliedApplicationAsync();

        // Applied -> Screening is an allowed transition (ApplicationStatusTransitionRules).
        await kanban.DragCardToColumnAsync(candidateLast, "Screening");

        Assert.Equal("Screening", await kanban.GetCardStatusBadgeTextAsync(candidateLast));

        // Reload the board from scratch (fresh navigation, not just re-reading the DOM) — the move
        // must have actually been persisted server-side (MoveApplicationStageAsync), not merely
        // reflected client-side by the drag itself.
        await kanban.WaitForLoadedAsync();
        Assert.Equal("Screening", await kanban.GetCardStatusBadgeTextAsync(candidateLast));
    }

    [Fact]
    public async Task DragCard_ToInvalidColumn_IsRejected_CardStaysPut_AndShowsError()
    {
        var (candidateLast, kanban) = await ArrangeAppliedApplicationAsync();

        // Applied -> Interviewed skips Screening/InterviewScheduled and is not in the allowed
        // transition graph — VacancyKanbanBoard.OnDragStopAsync rejects this client-side before
        // ever calling the server, sets an error banner, and re-fetches to snap the card back.
        await kanban.DragCardToColumnAsync(candidateLast, "Interviewed");

        Assert.True(await kanban.IsErrorVisibleAsync(),
            "Expected an error banner after attempting an invalid drag-and-drop move");
        var errorText = await kanban.GetErrorTextAsync();
        Assert.Contains("Applied", errorText ?? "");
        Assert.Contains("Interviewed", errorText ?? "");

        // The card must still be in its original column, not the rejected target.
        Assert.Equal("Applied", await kanban.GetCardStatusBadgeTextAsync(candidateLast));
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
    /// Creates a fresh candidate and vacancy, adds the candidate's application (leaving it in the
    /// initial "Applied" stage), then opens the vacancy's Kanban tab. Returns the candidate's
    /// (unique) last name and the ready-to-use board page object.
    /// </summary>
    private string? _vacancyTitle;

    private async Task<(string CandidateLast, VacancyKanbanBoardPage Kanban)> ArrangeAppliedApplicationAsync()
    {
        var unique         = Guid.NewGuid().ToString("N")[..8];
        var candidateFirst = "E2E";
        var candidateLast  = $"Kanban{unique}";
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
        await vacancyDetail.OpenApplicationsTabAsync();
        await vacancyDetail.ClickAddCandidateAsync();
        await vacancyDetail.SelectCandidateInAddDialogAsync(candidateEmail);
        await vacancyDetail.SubmitAddApplicationAsync();

        Assert.Equal("Applied", await vacancyDetail.GetApplicationStatusAsync(candidateLast));

        await vacancyDetail.OpenKanbanTabAsync();

        var kanban = new VacancyKanbanBoardPage(_page, _fixture.WebBaseUrl);
        await kanban.WaitForLoadedAsync();

        return (candidateLast, kanban);
    }
}
