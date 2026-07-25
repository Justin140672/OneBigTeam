using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the Recruiter-only dashboard
/// (src/HR.Web/Components/Pages/Dashboards/RecruitmentDashboard.razor), reached via
/// "/dashboard/recruitment". The page guards on Session.IsRecruiter and redirects any other role
/// to Session.MyProfileUrl — this is a stricter gate than the widgets' own internal checks
/// (CanManageEmployees || IsRecruiter): an HR Administrator who is not also a Recruiter (e.g.
/// Laura Bennett) can no longer reach this route at all, unlike the pre-restructure single
/// dashboard where she saw the Recruitment widget via CanManageEmployees alone. Marcus Diallo is
/// the only seeded persona with the Recruiter role, so he is used for every "positive" scenario.
///
/// The exact Open Vacancies count is not asserted beyond "at least the seeded 'Senior Software
/// Engineer' vacancy" since other E2E tests in this suite create additional open vacancies
/// against the same shared Acme company.
/// </summary>
[Collection("E2E")]
public sealed class RecruitmentDashboardTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private const string MarcusEmail = "marcus.diallo@acme.example";
    private const string LauraEmail  = "laura.bennett@acme.example";
    private const string TomEmail    = "tom.williams@acme.example";

    [Fact]
    public async Task NonRecruiter_IsRedirectedAway_FromRecruitmentDashboard()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/dashboard/recruitment");

        await _page.WaitForURLAsync(new Regex(@"/employees/[0-9a-f-]{36}/profile"), new() { Timeout = 15_000 });
        Assert.DoesNotContain("/dashboard/recruitment", _page.Url);
    }

    [Fact]
    public async Task HrAdministrator_WithoutRecruiterRole_IsRedirectedAway_FromRecruitmentDashboard()
    {
        // Laura is an HrAdministrator (CanManageEmployees) but not a Recruiter — under the old
        // single dashboard she would have seen the Recruitment widget via CanManageEmployees
        // alone, but the new route-level guard on Session.IsRecruiter blocks her entirely.
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/dashboard/recruitment");

        await _page.WaitForURLAsync(new Regex(@"/employees/[0-9a-f-]{36}/profile"), new() { Timeout = 15_000 });
        Assert.DoesNotContain("/dashboard/recruitment", _page.Url);
    }

    [Fact]
    public async Task Recruiter_SeesAllRecruitmentWidgets()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new RecruitmentDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);
        await dashboard.GoToAsync();

        Assert.True(await dashboard.HasWidgetAsync("Hiring Pipeline"));
        Assert.True(await dashboard.HasWidgetAsync("New Hires"));
        Assert.True(await dashboard.HasWidgetAsync("Recruitment"));
        Assert.True(await dashboard.HasWidgetAsync("Upcoming Interviews"));
        Assert.True(await dashboard.HasWidgetAsync("Offers & Recent Hires"));
        Assert.True(await dashboard.HasWidgetAsync("Stale Vacancies"));
    }

    [Fact]
    public async Task HiringPipelineChart_Loads()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new RecruitmentDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);
        await dashboard.GoToAsync();

        await dashboard.WaitForHiringPipelineChartLoadedAsync();
    }

    [Fact]
    public async Task NewHiresTrendChart_Loads()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new RecruitmentDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);
        await dashboard.GoToAsync();

        await dashboard.WaitForNewHiresTrendChartLoadedAsync();
    }

    [Fact]
    public async Task RecruitmentWidget_ShowsAtLeastTheSeededOpenVacancy()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new RecruitmentDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);
        await dashboard.GoToAsync();

        var openVacancies = await dashboard.GetRecruitmentKpiValueAsync("Open Vacancies");

        Assert.True(openVacancies >= 1,
            $"Expected at least 1 open vacancy (the seeded 'Senior Software Engineer'), but the widget showed {openVacancies}");
    }

    [Fact]
    public async Task RecruitmentWidget_ShowsNonNegativeInterviewsTodayAndOutstandingFeedbackCounts()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new RecruitmentDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);
        await dashboard.GoToAsync();

        var interviewsToday     = await dashboard.GetRecruitmentKpiValueAsync("Interviews Today");
        var outstandingFeedback = await dashboard.GetRecruitmentKpiValueAsync("Outstanding Feedback Tasks");

        Assert.True(interviewsToday >= 0);
        Assert.True(outstandingFeedback >= 0);
    }

    [Fact]
    public async Task ClickingOpenVacanciesKpi_NavigatesToVacanciesList()
    {
        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard   = new RecruitmentDashboardPage(_page, _fixture.WebBaseUrl);
        var vacancyList = new VacancyListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);
        await dashboard.GoToAsync();

        await dashboard.ClickOpenVacanciesKpiAsync();

        Assert.Contains("/vacancies", _page.Url);
        Assert.True(await vacancyList.HasVacancyAsync("Senior Software Engineer"),
            "Expected the seeded 'Senior Software Engineer' vacancy to be visible after navigating from the widget");
    }

    [Fact]
    public async Task ClickingVacanciesButton_NavigatesToVacanciesList()
    {
        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard   = new RecruitmentDashboardPage(_page, _fixture.WebBaseUrl);
        var vacancyList = new VacancyListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);
        await dashboard.GoToAsync();

        await dashboard.ClickVacanciesButtonAsync();

        Assert.Contains("/vacancies", _page.Url);
        Assert.True(await vacancyList.HasVacancyAsync("Senior Software Engineer"),
            "Expected the seeded 'Senior Software Engineer' vacancy to be visible after navigating from the dashboard button");
    }

    [Fact]
    public async Task ClickingCandidatesButton_NavigatesToCandidatesList()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new RecruitmentDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);
        await dashboard.GoToAsync();

        await dashboard.ClickCandidatesButtonAsync();

        Assert.Contains("/candidates", _page.Url);
    }

    [Fact]
    public async Task UpcomingInterviewsWidget_LoadsWithoutError()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new RecruitmentDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);
        await dashboard.GoToAsync();

        await dashboard.WaitForWidgetLoadedAsync("Upcoming Interviews");
        await dashboard.GetUpcomingInterviewCandidateNamesAsync();
    }

    [Fact]
    public async Task OffersAwaitingResponseWidget_LoadsWithoutError()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new RecruitmentDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);
        await dashboard.GoToAsync();

        await dashboard.WaitForWidgetLoadedAsync("Offers & Recent Hires");
    }

    [Fact]
    public async Task StaleVacanciesWidget_LoadsWithoutError()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new RecruitmentDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);
        await dashboard.GoToAsync();

        await dashboard.WaitForWidgetLoadedAsync("Stale Vacancies");
    }
}
