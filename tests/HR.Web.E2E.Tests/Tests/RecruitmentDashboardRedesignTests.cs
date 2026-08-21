using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the visual redesign of the Recruiter-only dashboard
/// (src/HR.Web/Components/Pages/Dashboards/RecruitmentDashboard.razor, reached via
/// "/dashboard/recruitment") — header + KPI summary tiles, the Pipeline/Activity/Insights nav tabs,
/// the consolidated Pipeline toolbar (vacancy picker, candidate search, "show closed candidates"
/// toggle), and the embedded Kanban board. Only markup/CSS changed in the redesign (see
/// RecruitmentDashboardTests for the pre-existing per-widget/chart coverage, updated alongside this
/// file to switch to the Activity/Insights tab first since those widgets now render behind tabs
/// rather than always-on).
///
/// Uses the seeded Acme company (00000000-0000-0000-0000-000000000001) and Marcus Diallo (the only
/// seeded Recruiter persona), consistent with RecruitmentDashboardTests and VacancyKanbanBoardTests.
/// </summary>
public sealed class RecruitmentDashboardRedesignTests(RecruiterPersonaFixture fixture) : RoleE2ETestBase<RecruiterPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string MarcusEmail = "marcus.diallo@acme.example";

    [Fact]
    public async Task Dashboard_ShowsHeaderTitleAndSummary_AndKpiTiles_AndNavTabs()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new RecruitmentDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);
        await dashboard.GoToAsync();

        Assert.Equal("Recruitment", (await dashboard.GetHeaderTitleAsync()).Trim());

        // HeaderSummary() renders "{N} open vacancy/vacancies · {M} candidate/candidates in
        // progress" (RecruitmentDashboard.razor's Pluralize helper).
        var summary = await dashboard.GetHeaderSummaryAsync();
        Assert.Matches(@"\d+ open vacanc(y|ies) · \d+ candidates? in progress", summary);

        // KPI summary tiles (RecruitmentSummaryTile row) — at least the seeded open vacancy should
        // push "Open vacancies" to 1 or more.
        var openVacancies = await dashboard.GetSummaryTileValueAsync("Open vacancies");
        Assert.True(openVacancies >= 1,
            $"Expected at least 1 open vacancy (the seeded 'Senior Software Engineer'), but the tile showed {openVacancies}");

        // Other tiles should render with a non-negative numeric value rather than being absent.
        Assert.True(await dashboard.GetSummaryTileValueAsync("New applications") >= 0);
        Assert.True(await dashboard.GetSummaryTileValueAsync("Interviews requiring action") >= 0);
        Assert.True(await dashboard.GetSummaryTileValueAsync("Offers awaiting response") >= 0);
        Assert.True(await dashboard.GetSummaryTileValueAsync("Stale vacancies") >= 0);

        // Nav tabs — Pipeline is the default active tab.
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

        // Pipeline (default): the toolbar/vacancy picker is present.
        await Assertions.Expect(_page.Locator(".recruitment-dashboard-toolbar")).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // Switch to Activity.
        await dashboard.SwitchToTabAsync(RecruitmentDashboardPage.Tab.Activity);
        Assert.True(await dashboard.IsTabActiveAsync(RecruitmentDashboardPage.Tab.Activity));
        Assert.False(await dashboard.IsTabActiveAsync(RecruitmentDashboardPage.Tab.Pipeline));
        await Assertions.Expect(_page.Locator("section[aria-label='Recruitment activity']")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        Assert.True(await dashboard.HasWidgetAsync("Recruitment"));

        // Switch to Insights.
        await dashboard.SwitchToTabAsync(RecruitmentDashboardPage.Tab.Insights);
        Assert.True(await dashboard.IsTabActiveAsync(RecruitmentDashboardPage.Tab.Insights));
        Assert.False(await dashboard.IsTabActiveAsync(RecruitmentDashboardPage.Tab.Activity));
        await Assertions.Expect(_page.Locator("section[aria-label='Recruitment insights']")).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // Back to Pipeline — toolbar should reappear.
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

        await dashboard.FillBoardSearchAsync("NoSuchApplicantXyz");
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

    /// <summary>
    /// Creates a fresh candidate and open vacancy with an application on it (unique names per run,
    /// same shape as VacancyKanbanBoardTests' identical helper) so the dashboard's vacancy picker
    /// and board have deterministic data to select and assert against, independent of other tests
    /// sharing the seeded Acme company.
    /// </summary>
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

        return (vacancyTitle, candidateLast);
    }
}
