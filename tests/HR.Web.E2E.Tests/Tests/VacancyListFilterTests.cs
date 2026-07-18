using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the "Vacancy - Position Profile relationship" epic's new server-side filter
/// dropdowns above the vacancy list grid — Position Profile and Department (see
/// VacancyList.razor's two new SfDropDownList controls, wired to reload the grid via
/// SearchPageBase.LoadAsync on ValueChange). Neither dropdown carries an explicit
/// data-testid, so VacancyListPage scopes them by their ".col-md-3" wrapper's label text.
/// </summary>
[Collection("E2E")]
public sealed class VacancyListFilterTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string MarcusEmail = "marcus.diallo@acme.example";

    [Fact]
    public async Task VacancyList_FilterByPositionProfile_NarrowsResults()
    {
        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var vacancyList = new VacancyListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await vacancyList.GoToAsync(AcmeId);

        var unfilteredCount = await vacancyList.GetVisibleRowCountAsync();

        await vacancyList.SelectPositionProfileFilterAsync("Senior Software Engineer");

        Assert.True(await vacancyList.HasVacancyAsync("Senior Software Engineer"),
            "Expected the 'Senior Software Engineer' vacancy to remain visible after filtering by its own Position Profile");
        Assert.False(await vacancyList.HasVacancyAsync("HR Business Partner"),
            "Expected 'HR Business Partner' to be filtered out when filtering by the 'Senior Software Engineer' Position Profile");

        var filteredCount = await vacancyList.GetVisibleRowCountAsync();
        Assert.True(filteredCount <= unfilteredCount,
            "Expected filtering by Position Profile to narrow (or leave unchanged) the visible row count");

        // Clearing the filter restores the full, unfiltered list.
        await vacancyList.ClearPositionProfileFilterAsync();
        Assert.True(await vacancyList.HasVacancyAsync("HR Business Partner"),
            "Expected 'HR Business Partner' to reappear once the Position Profile filter is cleared");
    }

    [Fact]
    public async Task VacancyList_FilterByDepartment_NarrowsResults()
    {
        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var vacancyList = new VacancyListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await vacancyList.GoToAsync(AcmeId);

        // "Senior Software Engineer" is seeded under the Engineering department (see
        // EmployeesModule.SeedEmployeesAsync) — filtering by Engineering should keep it visible.
        await vacancyList.SelectDepartmentFilterAsync("Engineering");

        Assert.True(await vacancyList.HasVacancyAsync("Senior Software Engineer"),
            "Expected the 'Senior Software Engineer' vacancy to remain visible after filtering by the Engineering department");

        await vacancyList.ClearDepartmentFilterAsync();
        Assert.True(await vacancyList.HasVacancyAsync("HR Business Partner"),
            "Expected 'HR Business Partner' to reappear once the Department filter is cleared");
    }

    [Fact]
    public async Task VacancyList_CombiningBothFilters_ReloadsWithoutError()
    {
        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var vacancyList = new VacancyListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await vacancyList.GoToAsync(AcmeId);

        await vacancyList.SelectPositionProfileFilterAsync("Senior Software Engineer");
        await vacancyList.SelectDepartmentFilterAsync("Engineering");

        Assert.Equal("Senior Software Engineer", await vacancyList.GetPositionProfileFilterTextAsync());
        Assert.Equal("Engineering", await vacancyList.GetDepartmentFilterTextAsync());
        Assert.True(await vacancyList.HasVacancyAsync("Senior Software Engineer"),
            "Expected the matching vacancy to remain visible when both filters are set consistently");
    }
}
