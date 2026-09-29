using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

public sealed class VacancyPublishTests(RecruiterPersonaFixture fixture) : RoleE2ETestBase<RecruiterPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string MarcusEmail = "marcus.diallo@acme.example";
    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task NewDraftVacancy_ShowsPublishButton_AndHidesApplicationsInterviewsTabs()
    {
        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var vacancyList   = new VacancyListPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        var profileTitle = await PositionProfileTestHelpers.CreateUniquePositionProfileAsync(
            _page, _fixture.WebBaseUrl, AcmeId, login, LauraEmail, MarcusEmail);

        var vacancyTitle = $"E2E Publish {Guid.NewGuid().ToString("N")[..8]}";
        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickNewVacancyAsync();
        await vacancyDetail.FillTitleAsync(vacancyTitle);
        await vacancyDetail.SelectPositionProfileAsync(profileTitle);
        await vacancyDetail.SelectHiringManagerAsync("James");
        await vacancyDetail.SaveNewVacancyAsync();

        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickVacancyAsync(vacancyTitle);

        Assert.True(await vacancyDetail.IsPublishButtonVisibleAsync(),
            "A newly-created Draft vacancy should show the Publish Vacancy button");

        Assert.False(await vacancyDetail.HasTabAsync("Applications"));
        Assert.False(await vacancyDetail.HasTabAsync("Interviews"));
    }

    [Fact]
    public async Task PublishVacancy_MovesDraftToOpen_AndRevealsApplicationsInterviewsTabs()
    {
        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var vacancyList   = new VacancyListPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        var profileTitle = await PositionProfileTestHelpers.CreateUniquePositionProfileAsync(
            _page, _fixture.WebBaseUrl, AcmeId, login, LauraEmail, MarcusEmail);

        var vacancyTitle = $"E2E Publish {Guid.NewGuid().ToString("N")[..8]}";
        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickNewVacancyAsync();
        await vacancyDetail.FillTitleAsync(vacancyTitle);
        await vacancyDetail.SelectPositionProfileAsync(profileTitle);
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

        await vacancyList.GoToAsync(AcmeId);
        Assert.True(await vacancyList.HasVacancyAsync(vacancyTitle));
    }
}
