using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the recruitment Kanban board / Recruitment Dashboard redesign (VacancyKanbanBoard.razor,
/// KanbanApplicantCard.razor, RecruitmentDashboard.razor): the narrower-viewport toolbar layout, the
/// Board/List view toggle's aria-pressed state, the "No candidates at this stage" empty-column copy,
/// the new keyboard-accessible "Move to stage…" card menu (an alternative to dragging, sharing the
/// same server-side MoveApplicationStageAsync call and validation as the drag path — see
/// VacancyKanbanBoard.MoveApplicationAsync's remarks), and the "dragging" CSS class lifecycle on the
/// board's columns container.
///
/// Follows VacancyKanbanBoardTests' exact fixture/persona/gating pattern: RecruiterPersonaFixture,
/// Marcus Diallo (Recruiter) against the seeded Acme company, and the same shared
/// CrossUserVacancyTestBase.GateInstance serialization since these tests also read the shared,
/// DisplayOrder-ranked seeded stage list (RecruitmentStageManagementTests mutates that same list).
/// </summary>
public sealed class VacancyKanbanBoardRedesignTests(RecruiterPersonaFixture fixture) : RoleE2ETestBase<RecruiterPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string MarcusEmail = "marcus.diallo@acme.example";

    // RecruitmentStageSeeder.BuildDefaultStages (see VacancyKanbanBoardTests' identical constants).
    private const string InitialStage  = "Application Received";
    private const string SecondStage   = "CV Review";
    private const string TerminalHired = "Hired";

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
    public async Task NarrowViewport_PipelineToolbar_RemainsUsable()
    {
        var (vacancyTitle, candidateLast) = await ArrangeAppliedApplicationForDashboardAsync();

        var dashboard = new RecruitmentDashboardPage(_page, _fixture.WebBaseUrl);

        // RecruitmentDashboard.razor.css's new @media (max-width:700px) breakpoint — 700px itself is
        // the edge of that breakpoint (an off-by-one on the max-width boundary would only be visible
        // just above/below it), but the acceptance criterion here is "still usable at narrow widths",
        // so a value comfortably inside the breakpoint (390px, a common mobile viewport width) is
        // used to avoid the assertion depending on which side of an exact boundary CSS treats
        // inclusively.
        await _page.SetViewportSizeAsync(390, 844);

        await dashboard.GoToAsync();

        // Toolbar elements must still be visible AND interactable at this width, not merely present
        // in the DOM (e.g. hidden via display:none or collapsed to zero width would still "exist").
        var vacancyPicker = _page.Locator(".recruitment-dashboard-vacancy-picker");
        await vacancyPicker.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await DropDownSelector.SelectAsync(_page, vacancyPicker, vacancyTitle);

        var kanban = new VacancyKanbanBoardPage(_page, _fixture.WebBaseUrl);
        await kanban.WaitForLoadedAsync();

        // Search box: fill it and confirm the filter actually applies (proves it's interactable, not
        // just visible).
        await dashboard.FillBoardSearchAsync(candidateLast);
        Assert.True(await kanban.HasCardForNameAsync(candidateLast),
            "Expected the search box to remain fillable and functional at a narrow (390px) viewport");

        // View toggle: still clickable and switches views.
        var viewToggle = _page.Locator("[data-testid='recruitment-view-toggle']");
        await Assertions.Expect(viewToggle).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await dashboard.SwitchToListViewAsync();
        await Assertions.Expect(_page.Locator(".dashboard-scroll-x")).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Fact]
    public async Task ViewToggle_AriaPressedState_MovesBetweenBoardAndList()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new RecruitmentDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);
        await dashboard.GoToAsync();

        var toggleGroup = _page.Locator("[data-testid='recruitment-view-toggle']");
        Assert.Equal("group", await toggleGroup.GetAttributeAsync("role"));

        var boardBtn = _page.Locator("[data-testid='recruitment-view-board-btn']");
        var listBtn  = _page.Locator("[data-testid='recruitment-view-list-btn']");

        // Board is the default view.
        Assert.Equal("true", await boardBtn.GetAttributeAsync("aria-pressed"));
        Assert.Equal("false", await listBtn.GetAttributeAsync("aria-pressed"));

        await dashboard.SwitchToListViewAsync();

        Assert.Equal("false", await boardBtn.GetAttributeAsync("aria-pressed"));
        Assert.Equal("true", await listBtn.GetAttributeAsync("aria-pressed"));

        // And back — the negated branch (Board re-selected after having been off) is exercised too.
        await dashboard.SwitchToBoardViewAsync();

        Assert.Equal("true", await boardBtn.GetAttributeAsync("aria-pressed"));
        Assert.Equal("false", await listBtn.GetAttributeAsync("aria-pressed"));

        Assert.Equal("group", await toggleGroup.GetAttributeAsync("role"));
    }

    [Fact]
    public async Task EmptyColumn_ShowsNoCandidatesAtThisStageCopy()
    {
        var (candidateLast, kanban) = await ArrangeAppliedApplicationAsync();

        // The freshly created application sits on InitialStage — Hired has nothing on this vacancy's
        // board yet, so its column must render the empty-state text rather than any cards.
        Assert.Equal(0, await kanban.GetColumnCountAsync(TerminalHired));

        var emptyText = await kanban.GetColumnEmptyTextAsync(TerminalHired);
        Assert.Equal("No candidates at this stage", emptyText);

        // Sanity: the initial stage (which does have a card) must NOT render the empty-state text.
        Assert.Null(await kanban.GetColumnEmptyTextAsync(InitialStage));
        Assert.True(await kanban.HasCardForNameAsync(candidateLast));
    }

    [Fact]
    public async Task MoveToStageMenu_KeyboardOnly_MovesApplicationToTargetStage_AndPersistsAcrossReload()
    {
        var (candidateLast, kanban) = await ArrangeAppliedApplicationAsync();

        Assert.True(await kanban.IsCardInColumnAsync(candidateLast, InitialStage),
            $"Expected the freshly created application to start on '{InitialStage}'");

        // Keyboard-only: FocusAsync + Enter to open the menu, FocusAsync + Enter on the target
        // stage's menu item to select it — no mouse involved anywhere in this call.
        await kanban.MoveToStageViaKeyboardAsync(candidateLast, SecondStage);

        Assert.True(await kanban.IsCardInColumnAsync(candidateLast, SecondStage),
            $"Expected the keyboard-driven move to land the card on '{SecondStage}'");
        Assert.False(await kanban.IsCardInColumnAsync(candidateLast, InitialStage),
            $"Expected the card to no longer be reported on '{InitialStage}' after the keyboard move");

        // Re-navigate to the standalone board (fresh GetRecruitmentKanbanHandler query) to confirm
        // the move was actually persisted server-side, not just reflected in leftover client state —
        // same persistence-check pattern as DraggingCard_MovesApplicationToTargetStage_AndPersistsAcrossReload.
        var vacancyId = ExtractVacancyIdFromUrl(_page.Url);
        var reloadedKanban = new VacancyKanbanBoardPage(_page, _fixture.WebBaseUrl);
        await reloadedKanban.GoToStandaloneAsync(AcmeId, vacancyId);

        Assert.True(await reloadedKanban.IsCardInColumnAsync(candidateLast, SecondStage),
            $"Expected the move to '{SecondStage}' to have persisted server-side after a fresh page load");
        Assert.False(await reloadedKanban.IsCardInColumnAsync(candidateLast, InitialStage),
            $"Expected the card to no longer be reported on '{InitialStage}' after a fresh page load");
    }

    [Fact]
    public async Task MoveToStageMenu_OnTerminalStageCard_IsRejectedByServer_AndSurfacesTheSameErrorBannerAsDrag()
    {
        var (candidateLast, kanban) = await ArrangeAppliedApplicationAsync();

        // First get the card onto a terminal stage via the (already-covered) drag path.
        await kanban.DragCardToColumnAsync(candidateLast, TerminalHired);
        Assert.True(await kanban.IsCardInColumnAsync(candidateLast, TerminalHired),
            $"Sanity check: expected the drag to land the card on '{TerminalHired}' before attempting a further move");

        // Now attempt a further move via the keyboard "Move to stage…" menu on the now-terminal
        // card — MoveApplicationStageHandler rejects any move off a terminal stage, and this
        // assertion proves that rejection surfaces through the exact same error banner
        // (data-testid="kanban-error") the drag path uses, i.e. the keyboard path is validated by
        // the same server-side rule rather than a separate/looser client-side one.
        await kanban.MoveToStageViaKeyboardAsync(candidateLast, SecondStage);

        Assert.True(await kanban.IsErrorVisibleAsync(),
            "Expected attempting to move a terminal-stage application via the keyboard menu to surface the same error banner the drag path uses");

        // And the card must still be reported on the terminal stage — the rejected move must not
        // have silently taken effect client-side.
        Assert.True(await kanban.IsCardInColumnAsync(candidateLast, TerminalHired),
            $"Expected the card to remain on '{TerminalHired}' after the server rejected the further move");
    }

    [Fact]
    public async Task DraggingClass_IsAppliedDuringDrag_AndRemovedAfterDragEnds()
    {
        var (candidateLast, kanban) = await ArrangeAppliedApplicationAsync();

        // Before any drag, the columns container must not carry the "dragging" class.
        Assert.False(await kanban.HasDraggingClassAsync(),
            "Expected the 'dragging' class to be absent before any drag has started");

        // Playwright's Locator.DragToAsync dispatches the full dragstart→dragend sequence fast
        // enough that the intermediate "dragging" class is not reliably observable through it (it
        // completes the whole gesture before a subsequent assertion can run). To actually observe
        // the class mid-drag, dispatch a bare "dragstart" DOM event manually (without ever
        // dispatching "drop"), assert the class, then dispatch "dragend" to return to rest — see
        // VacancyKanbanBoardPage.ObserveDraggingClassDuringManualDragAsync's remarks. This never
        // drops the card onto a column, so no server-side move happens as a side effect.
        var wasDraggingMidGesture = await kanban.ObserveDraggingClassDuringManualDragAsync(candidateLast);
        Assert.True(wasDraggingMidGesture,
            "Expected the 'dragging' class to be present on the columns container while a drag is in progress");

        // After dragend, the class must be removed again.
        Assert.False(await kanban.HasDraggingClassAsync(),
            "Expected the 'dragging' class to be removed once the drag ends");

        // The card itself must not have moved — this was never a real drop.
        Assert.True(await kanban.IsCardInColumnAsync(candidateLast, InitialStage),
            "Expected the manual dragstart/dragend probe to have no effect on the card's actual stage");
    }

    /// <summary>
    /// Same shape as VacancyKanbanBoardTests.ExtractVacancyIdFromUrl — extracts the vacancy id from
    /// either the standalone Kanban route or the Vacancy Detail "Kanban" tab route, both of which
    /// carry it in the same URL segment.
    /// </summary>
    private static Guid ExtractVacancyIdFromUrl(string url)
    {
        var match = System.Text.RegularExpressions.Regex.Match(url, @"/vacancies/([0-9a-fA-F-]{36})");
        if (!match.Success)
            throw new InvalidOperationException($"Could not extract a vacancy id from URL '{url}'.");
        return Guid.Parse(match.Groups[1].Value);
    }

    /// <summary>
    /// Same shape as VacancyKanbanBoardTests.ArrangeAppliedApplicationAsync — creates a fresh
    /// candidate and vacancy, adds the candidate's application (leaving it on the seeded initial
    /// stage), then opens the vacancy's Kanban tab. Returns the candidate's (unique) last name and
    /// the ready-to-use board page object.
    /// </summary>
    private async Task<(string CandidateLast, VacancyKanbanBoardPage Kanban)> ArrangeAppliedApplicationAsync()
    {
        var (vacancyTitle, candidateLast) = await ArrangeAppliedApplicationForDashboardAsync();

        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);
        await vacancyDetail.OpenKanbanTabAsync();

        var kanban = new VacancyKanbanBoardPage(_page, _fixture.WebBaseUrl);
        await kanban.WaitForLoadedAsync();

        return (candidateLast, kanban);
    }

    /// <summary>
    /// Variant used by NarrowViewport_PipelineToolbar_RemainsUsable, which needs the vacancy title
    /// (to select it in the dashboard's vacancy picker) rather than an already-opened Kanban tab.
    /// Leaves the caller on the Vacancy Detail page after adding the application.
    /// </summary>
    private async Task<(string VacancyTitle, string CandidateLast)> ArrangeAppliedApplicationForDashboardAsync()
    {
        var unique         = Guid.NewGuid().ToString("N")[..8];
        var candidateFirst = "E2E";
        var candidateLast  = $"KanbanRedesign{unique}";
        var candidateName  = $"{candidateFirst} {candidateLast}";
        var candidateEmail = $"e2e.kanbanredesign{unique}@example.com";
        var vacancyTitle   = $"E2E Kanban Redesign Role {unique}";

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

        return (vacancyTitle, candidateLast);
    }
}
