using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the report catalog landing page (/companies/{companyId}/reporting —
/// ReportCatalogPage.razor): card rendering, search filtering, favourites (server-persisted via
/// ReportingService.GetReportFavouritesAsync/Add/RemoveReportFavouriteAsync — previously
/// localStorage-backed), and navigation into the Employee Directory, Employee Starter, Employee
/// Leaver, Leave Summary, Leave Calendar, Sickness, Recruitment Pipeline, Vacancy Performance,
/// Probation, Onboarding Progress, Offboarding Progress, Document Compliance and Company Document
/// Acknowledgement reports. Access-control coverage (non-HR persona not
/// seeing the Employee Directory card, direct-URL 403 handling on the report page itself) lives
/// in <see cref="EmployeeDirectoryReportTests"/>.
/// </summary>
[Collection("E2E")]
public sealed class ReportCatalogTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example"; // HR Administrator

    [Fact]
    public async Task CatalogPage_Loads_WithEmployeeDirectoryCardVisible()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var catalog = new ReportCatalogPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await catalog.GoToAsync(AcmeId);

        Assert.True(await catalog.HasCardAsync("Employee Directory"),
            "Expected the Employee Directory catalog card to be visible for an HR Administrator");

        var description = await catalog.GetCardDescriptionAsync("Employee Directory");
        Assert.Contains("employee directory", description ?? "", StringComparison.OrdinalIgnoreCase);

        Assert.True(await catalog.IsCardClickableAsync("Employee Directory"),
            "Expected the Employee Directory card to be clickable (no 'Coming soon' badge)");

        // Other phase-1 catalog entries are rendered but not yet clickable.
        Assert.False(await catalog.IsCardClickableAsync("HR Headcount Summary"),
            "Expected the HR Headcount Summary card to show a 'Coming soon' badge");
    }

    [Fact]
    public async Task SearchBox_FiltersCardsByNameOrDescription()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var catalog = new ReportCatalogPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await catalog.GoToAsync(AcmeId);

        var countBeforeSearch = await catalog.GetVisibleCardCountAsync();
        Assert.True(countBeforeSearch > 1, "Expected more than one catalog card before searching");

        await catalog.SearchAsync("Employee Directory");

        Assert.Equal(1, await catalog.GetVisibleCardCountAsync());
        Assert.True(await catalog.HasCardAsync("Employee Directory"));
    }

    [Fact]
    public async Task FavouriteToggle_PersistsAcrossReload_AndSortsFirstInCategory()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var catalog = new ReportCatalogPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await catalog.GoToAsync(AcmeId);

        // "HR Headcount Summary" and "Employee Directory" are both in the "Hr" category, with
        // "Employee Directory" sorting first alphabetically by default (E < H) — favouriting
        // "HR Headcount Summary" should move it ahead of "Employee Directory".
        Assert.False(await catalog.IsFavouritedAsync("HR Headcount Summary"));

        await catalog.ClickFavouriteAsync("HR Headcount Summary");
        Assert.True(await catalog.IsFavouritedAsync("HR Headcount Summary"));

        var titlesAfterFavouriting = await catalog.GetCardTitlesInCategoryAsync("Hr");
        Assert.Equal("HR Headcount Summary", titlesAfterFavouriting.FirstOrDefault());

        // Reload — favourites now round-trip through the server (ReportingService.AddReportFavouriteAsync
        // / GetReportFavouritesAsync), not localStorage, so a plain reload (not just client-side
        // navigation) proves the toggle actually persisted server-side rather than only updating
        // in-memory component state.
        await _page.ReloadAsync();
        await _page.WaitForSelectorAsync(".report-catalog-card, .hr-empty-state", new() { Timeout = 20_000 });

        Assert.True(await catalog.IsFavouritedAsync("HR Headcount Summary"),
            "Expected the favourite to survive a page reload");

        var titlesAfterReload = await catalog.GetCardTitlesInCategoryAsync("Hr");
        Assert.Equal("HR Headcount Summary", titlesAfterReload.FirstOrDefault());
    }

    [Fact]
    public async Task ClickingEmployeeDirectoryCard_NavigatesToReportPage_WithGridColumns()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var catalog = new ReportCatalogPage(_page, _fixture.WebBaseUrl);
        var report = new EmployeeDirectoryReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await catalog.GoToAsync(AcmeId);
        await catalog.ClickCardAsync("Employee Directory");

        await _page.WaitForURLAsync("**/reporting/employee-directory", new() { Timeout = 15_000 });

        var headers = await report.GetColumnHeadersAsync();
        Assert.Contains(headers, h => h.Contains("Employee Number"));
        Assert.Contains(headers, h => h.Contains("Name"));
        Assert.Contains(headers, h => h.Contains("Department"));
        Assert.Contains(headers, h => h.Contains("Manager"));
        Assert.Contains(headers, h => h.Contains("Status"));
        Assert.Contains(headers, h => h.Contains("Email"));
    }

    [Theory]
    [InlineData("Employee Starters", "employee-starters")]
    [InlineData("Employee Leavers", "employee-leavers")]
    [InlineData("Leave Summary", "leave-summary")]
    [InlineData("Leave Calendar", "leave-calendar")]
    [InlineData("Sickness Report", "sickness")]
    [InlineData("Recruitment Pipeline Report", "recruitment-pipeline")]
    [InlineData("Vacancy Performance Report", "vacancy-performance")]
    [InlineData("Probation Report", "probation")]
    [InlineData("Onboarding Progress Report", "onboarding-progress")]
    [InlineData("Offboarding Progress Report", "offboarding-progress")]
    [InlineData("Document Compliance Report", "document-compliance")]
    [InlineData("Company Document Acknowledgement Report", "document-acknowledgement")]
    public async Task NewReportCard_IsClickable_AndNavigatesToCorrectRoute(string cardTitleFragment, string routeSlug)
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var catalog = new ReportCatalogPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await catalog.GoToAsync(AcmeId);

        Assert.True(await catalog.HasCardAsync(cardTitleFragment),
            $"Expected the {cardTitleFragment} catalog card to be visible for an HR Administrator");
        Assert.True(await catalog.IsCardClickableAsync(cardTitleFragment),
            $"Expected the {cardTitleFragment} card to be clickable (no 'Coming soon' badge)");

        await catalog.ClickCardAsync(cardTitleFragment);

        await _page.WaitForURLAsync($"**/reporting/{routeSlug}", new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Favourites persisting across reload is covered generically by
    /// <see cref="FavouriteToggle_PersistsAcrossReload_AndSortsFirstInCategory"/> above using the
    /// "HR Headcount Summary" catalog entry (a "Coming soon" card in the same "Hr" category as
    /// Employee Directory — chosen there so sort-order assertions aren't entangled with a card
    /// that also navigates). This test instead proves the same server round-trip specifically for
    /// one of the newly added, now-clickable report cards, via a full navigation away (into the
    /// report page itself) and back rather than a plain reload.
    /// </summary>
    [Fact]
    public async Task FavouritingNewReportCard_PersistsAcrossNavigationAwayAndBack()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var catalog = new ReportCatalogPage(_page, _fixture.WebBaseUrl);
        var report = new EmployeeStarterReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await catalog.GoToAsync(AcmeId);

        Assert.False(await catalog.IsFavouritedAsync("Employee Starters"));

        await catalog.ClickFavouriteAsync("Employee Starters");
        Assert.True(await catalog.IsFavouritedAsync("Employee Starters"));

        // Navigate away into the report page itself, then back to the catalog — proves the
        // favourite round-tripped through the server rather than only surviving in the same
        // component instance's in-memory state.
        await catalog.ClickCardAsync("Employee Starters");
        await _page.WaitForURLAsync("**/reporting/employee-starters", new() { Timeout = 15_000 });
        Assert.False(await report.HasLoadErrorAsync());

        await catalog.GoToAsync(AcmeId);

        Assert.True(await catalog.IsFavouritedAsync("Employee Starters"),
            "Expected the favourite to survive navigating away to the report page and back");

        // Clean up so this test is repeatable against the shared, long-lived E2E dev database.
        await catalog.ClickFavouriteAsync("Employee Starters");
        Assert.False(await catalog.IsFavouritedAsync("Employee Starters"));
    }
}
