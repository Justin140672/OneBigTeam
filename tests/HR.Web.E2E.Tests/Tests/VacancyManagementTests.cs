using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies HR Administrator CRUD workflows for vacancies:
/// - Seeded vacancies appear in the list.
/// - A new vacancy can be created and appears in the list.
/// - Validation errors surface when required fields are missing.
/// - Plain employees cannot reach the vacancies page.
/// </summary>
[Collection("E2E")]
public sealed class VacancyManagementTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task VacancyList_ShowsSeededVacancies()
    {
        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var vacancyList = new VacancyListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await vacancyList.GoToAsync(AcmeId);

        Assert.True(await vacancyList.HasVacancyAsync("Senior Software Engineer"),
            "Expected 'Senior Software Engineer' in the vacancy list");
        Assert.True(await vacancyList.HasVacancyAsync("HR Business Partner"),
            "Expected 'HR Business Partner' in the vacancy list");
        Assert.True(await vacancyList.HasVacancyAsync("Product Designer"),
            "Expected 'Product Designer' in the vacancy list");
    }

    [Fact]
    public async Task CreateVacancy_AppearsInList()
    {
        var vacancyTitle = $"E2E Vacancy {Guid.NewGuid().ToString("N")[..8]}";

        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var vacancyList   = new VacancyListPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await vacancyList.GoToAsync(AcmeId);
        await vacancyList.ClickNewVacancyAsync();

        await vacancyDetail.FillTitleAsync(vacancyTitle);
        await vacancyDetail.FillLocationAsync("Remote");
        await vacancyDetail.SelectHiringManagerAsync("James");
        await vacancyDetail.SaveNewVacancyAsync();

        Assert.True(await vacancyList.HasVacancyAsync(vacancyTitle),
            $"Expected the new vacancy '{vacancyTitle}' to appear in the list after creation");
    }

    [Fact]
    public async Task CreateVacancy_WithEmptyTitle_ShowsValidationError()
    {
        var login         = new LoginPage(_page, _fixture.WebBaseUrl);
        var vacancyDetail = new VacancyDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await vacancyDetail.GoToNewAsync(AcmeId);

        // Leave the title empty and try to save.
        await _page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        await _page.WaitForFunctionAsync(
            "document.querySelector('.alert-danger, .validation-message') !== null " +
            "|| !window.location.href.includes('/vacancies/new')",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });

        Assert.Contains("/vacancies/new", _page.Url);
        Assert.True(await vacancyDetail.HasErrorAsync(),
            "Expected a validation error when saving a vacancy with no title");
    }

    [Fact]
    public async Task PlainEmployee_IsRedirectedAway_FromVacanciesPage()
    {
        const string tomEmail = "tom.williams@acme.example";

        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(tomEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/vacancies");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15_000 });

        var finalUrl = _page.Url;
        Assert.False(finalUrl.Contains("/vacancies"),
            $"Expected a plain employee to be redirected away from the vacancies page, but ended up at: {finalUrl}");
    }
}
