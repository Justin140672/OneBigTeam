using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class RecruitmentDashboardRedesignTests(RecruiterPersonaFixture fixture) : RoleE2ETestBase<RecruiterPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string MarcusEmail = "marcus.diallo@acme.example";
    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task Dashboard_ShowsHeaderTitleAndSummary_AndKpiTiles_AndNavTabs()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new RecruitmentDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);
        await dashboard.GoToAsync();

        Assert.Equal("Recruitment", (await dashboard.GetHeaderTitleAsync()).Trim());

        var summary = await dashboard.GetHeaderSummaryAsync();
        Assert.Matches(@"\d+ open vacanc(y|ies) · \d+ candidates? in progress", summary);

        var openVacancies = await dashboard.GetSummaryTileValueAsync("Open vacancies");
        Assert.True(openVacancies >= 1,
            $"Expected at least 1 open vacancy (the seeded 'Senior Software Engineer'), but the tile showed {openVacancies}");

        Assert.True(await dashboard.GetSummaryTileValueAsync("New applications") >= 0);
        Assert.True(await dashboard.GetSummaryTileValueAsync("Interviews requiring action") >= 0);
        Assert.True(await dashboard.GetSummaryTileValueAsync("Offers awaiting response") >= 0);
        Assert.True(await dashboard.GetSummaryTileValueAsync("Stale vacancies") >= 0);

        Assert.True(await dashboard.IsTabActiveAsync(RecruitmentDashboardPage.Tab.Pipeline));
        Assert.False(await dashboard.IsTabActiveAsync(RecruitmentDashboardPage.Tab.Activity));
        Assert.False(await dashboard.IsTabActiveAsync(RecruitmentDashboardPage.Tab.Insights));
    }

    [Fact]
    public async Task SwitchingTabs_UpdatesActiveState_AndSwapsVisibleContent()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new RecruitmentDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);
        await dashboard.GoToAsync();

        await Assertions.Expect(_page.Locator(".recruitment-dashboard-toolbar")).ToBeVisibleAsync(new() { Timeout = 15_000 });

        await dashboard.SwitchToTabAsync(RecruitmentDashboardPage.Tab.Activity);
        Assert.True(await dashboard.IsTabActiveAsync(RecruitmentDashboardPage.Tab.Activity));
        Assert.False(await dashboard.IsTabActiveAsync(RecruitmentDashboardPage.Tab.Pipeline));
        await Assertions.Expect(_page.Locator("section[aria-label='Recruitment activity']")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        Assert.True(await dashboard.HasWidgetAsync("Recruitment"));

        await dashboard.SwitchToTabAsync(RecruitmentDashboardPage.Tab.Insights);
        Assert.True(await dashboard.IsTabActiveAsync(RecruitmentDashboardPage.Tab.Insights));
        Assert.False(await dashboard.IsTabActiveAsync(RecruitmentDashboardPage.Tab.Activity));
        await Assertions.Expect(_page.Locator("section[aria-label='Recruitment insights']")).ToBeVisibleAsync(new() { Timeout = 15_000 });

        await dashboard.SwitchToTabAsync(RecruitmentDashboardPage.Tab.Pipeline);
        Assert.True(await dashboard.IsTabActiveAsync(RecruitmentDashboardPage.Tab.Pipeline));
        await Assertions.Expect(_page.Locator(".recruitment-dashboard-toolbar")).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Fact]
    public async Task PipelineToolbar_VacancyPicker_SelectsVacancy_AndRendersItsBoard()
    {
        var (vacancyTitle, candidateLast) = await ArrangeAppliedApplicationAsync();

        var dashboard = new RecruitmentDashboardPage(_page, _fixture.WebBaseUrl);
        await dashboard.GoToAsync();

        await dashboard.SelectVacancyAsync(vacancyTitle);

        var kanban = new VacancyKanbanBoardPage(_page, _fixture.WebBaseUrl);
        await kanban.WaitForLoadedAsync();

        Assert.True(await kanban.HasCardForNameAsync(candidateLast),
            "Expected the Kanban board to render the seeded candidate's card for the selected vacancy");
    }

    [Fact]
    public async Task PipelineToolbar_SearchBox_FiltersKanbanCards()
    {
        var (vacancyTitle, candidateLast) = await ArrangeAppliedApplicationAsync();

        var dashboard = new RecruitmentDashboardPage(_page, _fixture.WebBaseUrl);
        await dashboard.GoToAsync();
        await dashboard.SelectVacancyAsync(vacancyTitle);

        var kanban = new VacancyKanbanBoardPage(_page, _fixture.WebBaseUrl);
        await kanban.WaitForLoadedAsync();
        Assert.True(await kanban.HasCardForNameAsync(candidateLast));

        await dashboard.FillBoardSearchAsync("NoSuchCandidateXyz");
        Assert.False(await kanban.HasCardForNameAsync(candidateLast),
            "Expected the candidate's card to be hidden once the toolbar search term no longer matches");

        await dashboard.FillBoardSearchAsync(candidateLast);
        Assert.True(await kanban.HasCardForNameAsync(candidateLast),
            "Expected the candidate's card to reappear once the toolbar search term matches again");
    }

    [Fact]
    public async Task PipelineToolbar_ShowClosedCandidatesToggle_TogglesState()
    {
        var (vacancyTitle, _) = await ArrangeAppliedApplicationAsync();

        var dashboard = new RecruitmentDashboardPage(_page, _fixture.WebBaseUrl);
        await dashboard.GoToAsync();
        await dashboard.SelectVacancyAsync(vacancyTitle);

        var kanban = new VacancyKanbanBoardPage(_page, _fixture.WebBaseUrl);
        await kanban.WaitForLoadedAsync();

        Assert.False(await dashboard.IsShowClosedCandidatesCheckedAsync(),
            "Expected 'Show closed candidates' to default to unchecked");

        await dashboard.ToggleShowClosedCandidatesAsync();
        Assert.True(await dashboard.IsShowClosedCandidatesCheckedAsync());

        await dashboard.ToggleShowClosedCandidatesAsync();
        Assert.False(await dashboard.IsShowClosedCandidatesCheckedAsync());
    }

    [Fact]
    public async Task PipelineBoard_DraggingCard_MovesApplicationToTargetStage_AndPersistsAcrossReload()
    {
        // RecruitmentStageSeeder.BuildDefaultStages — same seeded stage set VacancyKanbanBoardTests
        // asserts against ("Application Received", "CV Review", "Interview", "Offer", "Hired",
        // "Rejected"); a freshly created Application always starts on "Application Received".
        const string InitialStage      = "Application Received";
        const string NonTerminalStage2 = "CV Review";

        var (vacancyTitle, candidateLast) = await ArrangeAppliedApplicationAsync();

        var dashboard = new RecruitmentDashboardPage(_page, _fixture.WebBaseUrl);
        await dashboard.GoToAsync();
        await dashboard.SelectVacancyAsync(vacancyTitle);

        var kanban = new VacancyKanbanBoardPage(_page, _fixture.WebBaseUrl);
        await kanban.WaitForLoadedAsync();

        Assert.True(await kanban.IsCardInColumnAsync(candidateLast, InitialStage),
            $"Expected the freshly created application to start on '{InitialStage}'");

        await kanban.DragCardToColumnAsync(candidateLast, NonTerminalStage2);

        Assert.True(await kanban.IsCardInColumnAsync(candidateLast, NonTerminalStage2),
            $"Expected the card for {candidateLast} to appear in the '{NonTerminalStage2}' column after the drag");
        Assert.False(await kanban.IsCardInColumnAsync(candidateLast, InitialStage),
            $"Expected the card for {candidateLast} to no longer be in the '{InitialStage}' column after the drag");

        await dashboard.GoToAsync();
        await dashboard.SelectVacancyAsync(vacancyTitle);

        var reloadedKanban = new VacancyKanbanBoardPage(_page, _fixture.WebBaseUrl);
        await reloadedKanban.WaitForLoadedAsync();

        Assert.True(await reloadedKanban.IsCardInColumnAsync(candidateLast, NonTerminalStage2),
            $"Expected the move to '{NonTerminalStage2}' to have persisted server-side after a fresh dashboard load");
        Assert.False(await reloadedKanban.IsCardInColumnAsync(candidateLast, InitialStage),
            $"Expected the card to no longer be reported on '{InitialStage}' after a fresh dashboard load");
    }

    [Fact]
    public async Task CreateVacancyButton_NavigatesToNewVacancyForm()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new RecruitmentDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);
        await dashboard.GoToAsync();

        await dashboard.ClickCreateVacancyAsync();

        await _page.WaitForURLAsync(new System.Text.RegularExpressions.Regex(@"/vacancies/new"), new() { Timeout = 15_000 });
        Assert.Contains("/vacancies/new", _page.Url);
    }

    [Fact]
    public async Task AddCandidateButton_NavigatesToNewCandidateForm()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new RecruitmentDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);
        await dashboard.GoToAsync();

        await dashboard.ClickAddCandidateAsync();

        await _page.WaitForURLAsync(new System.Text.RegularExpressions.Regex(@"/candidates/new"), new() { Timeout = 15_000 });
        Assert.Contains("/candidates/new", _page.Url);
    }

    private async Task<(string VacancyTitle, string CandidateLast)> ArrangeAppliedApplicationAsync()
    {
        var unique         = Guid.NewGuid().ToString("N")[..8];
        var candidateFirst = "E2E";
        var candidateLast  = $"Dash{unique}";
        var candidateName  = $"{candidateFirst} {candidateLast}";
        var candidateEmail = $"e2e.dash{unique}@example.com";
        var vacancyTitle   = $"E2E Dashboard Role {unique}";

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

        return (vacancyTitle, candidateLast);
    }
}
