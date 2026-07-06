using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the Recruitment dashboard widget: shown for HR Administrator (gated the same
/// way as the rest of the admin sidebar, on Session.CanManageEmployees), hidden for a plain
/// employee, and its three KPIs (Open Vacancies, Interviews Today, Outstanding Feedback
/// Tasks) render and the Open Vacancies KPI navigates to the vacancies list.
///
/// The exact Open Vacancies count is not asserted beyond "at least the seeded 'Senior
/// Software Engineer' vacancy" since other E2E tests in this suite create additional open
/// vacancies against the same shared Acme company.
/// </summary>
[Collection("E2E")]
public sealed class RecruitmentSummaryWidgetTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private const string LauraEmail = "laura.bennett@acme.example";
    private const string TomEmail   = "tom.williams@acme.example";

    [Fact]
    public async Task HrAdministrator_Sees_RecruitmentWidget()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new DashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await dashboard.GoToAsync();

        Assert.True(await dashboard.HasWidgetAsync("Recruitment"),
            "Expected the Recruitment widget to be visible for an HR Administrator");
    }

    [Fact]
    public async Task PlainEmployee_DoesNotSee_RecruitmentWidget()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new DashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);
        await dashboard.GoToAsync();

        Assert.False(await dashboard.HasWidgetAsync("Recruitment"),
            "Expected the Recruitment widget to be hidden for a plain employee");
    }

    [Fact]
    public async Task RecruitmentWidget_ShowsAtLeastTheSeededOpenVacancy()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new DashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await dashboard.GoToAsync();

        var openVacancies = await dashboard.GetRecruitmentKpiValueAsync("Open Vacancies");

        Assert.True(openVacancies >= 1,
            $"Expected at least 1 open vacancy (the seeded 'Senior Software Engineer'), but the widget showed {openVacancies}");
    }

    [Fact]
    public async Task RecruitmentWidget_ShowsNonNegativeInterviewsTodayAndOutstandingFeedbackCounts()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new DashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await dashboard.GoToAsync();

        var interviewsToday = await dashboard.GetRecruitmentKpiValueAsync("Interviews Today");
        var outstandingFeedback = await dashboard.GetRecruitmentKpiValueAsync("Outstanding Feedback Tasks");

        Assert.True(interviewsToday >= 0);
        Assert.True(outstandingFeedback >= 0);
    }

    [Fact]
    public async Task ClickingOpenVacanciesKpi_NavigatesToVacanciesList()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new DashboardPage(_page, _fixture.WebBaseUrl);
        var vacancyList = new VacancyListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await dashboard.GoToAsync();

        await dashboard.ClickOpenVacanciesKpiAsync();

        Assert.Contains("/vacancies", _page.Url);
        Assert.True(await vacancyList.HasVacancyAsync("Senior Software Engineer"),
            "Expected the seeded 'Senior Software Engineer' vacancy to be visible after navigating from the widget");
    }
}
