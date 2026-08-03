using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the "Publish Vacancy" button (VacancyDetail.razor's CanPublish/PublishVacancyAsync,
/// backed by the Recruitment module's PublishVacancy feature — POST
/// .../vacancies/{id}/publish, which calls Vacancy.Open() to move Draft/OnHold to Open). Before
/// this feature existed, a vacancy created via CreateVacancy stayed in Draft status forever —
/// there was no UI or API path to open one.
///
/// Uses Marcus Diallo (Recruiter role) — recruitment:manage is Recruiter-only (see
/// IdentityModule.AddRolePolicies), matching the convention in VacancyEditCloseBehaviorTests.
/// </summary>
[Collection("E2E")]
public sealed class VacancyPublishTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string MarcusEmail = "marcus.diallo@acme.example";

    [Fact]
    public async Task NewDraftVacancy_ShowsPublishButton_AndHidesApplicationsInterviewsKanbanTabs()
    {
        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var vacancyList   = new VacancyListPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        var vacancyTitle = $"E2E Publish {Guid.NewGuid().ToString("N")[..8]}";
        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickNewVacancyAsync();
        await vacancyDetail.FillTitleAsync(vacancyTitle);
        await vacancyDetail.SelectPositionProfileAsync("Senior Software Engineer");
        await vacancyDetail.SelectHiringManagerAsync("James");
        await vacancyDetail.SaveNewVacancyAsync();

        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickVacancyAsync(vacancyTitle);

        Assert.True(await vacancyDetail.IsPublishButtonVisibleAsync(),
            "A newly-created Draft vacancy should show the Publish Vacancy button");

        // Draft-status tabs are hidden entirely (see item #21 — VacancyDetail.razor's
        // "_vacancy.Status != 'Draft'" gate).
        Assert.False(await vacancyDetail.HasTabAsync("Applications"));
        Assert.False(await vacancyDetail.HasTabAsync("Interviews"));
        Assert.False(await vacancyDetail.HasTabAsync("Kanban"));
    }

    [Fact]
    public async Task PublishVacancy_MovesDraftToOpen_AndRevealsApplicationsInterviewsKanbanTabs()
    {
        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var vacancyList   = new VacancyListPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        var vacancyTitle = $"E2E Publish {Guid.NewGuid().ToString("N")[..8]}";
        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickNewVacancyAsync();
        await vacancyDetail.FillTitleAsync(vacancyTitle);
        await vacancyDetail.SelectPositionProfileAsync("Senior Software Engineer");
        await vacancyDetail.SelectHiringManagerAsync("James");
        await vacancyDetail.SaveNewVacancyAsync();

        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickVacancyAsync(vacancyTitle);

        await vacancyDetail.PublishVacancyAsync();

        Assert.Equal("Open", await vacancyDetail.GetStatusBadgeTextAsync());
        Assert.False(await vacancyDetail.IsPublishButtonVisibleAsync(),
            "Publish Vacancy should no longer be offered once the vacancy is Open");

        Assert.True(await vacancyDetail.HasTabAsync("Applications"));
        Assert.True(await vacancyDetail.HasTabAsync("Interviews"));
        Assert.True(await vacancyDetail.HasTabAsync("Kanban"));

        // The Vacancy List's "Show active" filter (item #11) should now surface it too.
        await vacancyList.GoToAsync(AcmeId);
        Assert.True(await vacancyList.HasVacancyAsync(vacancyTitle));
    }
}
