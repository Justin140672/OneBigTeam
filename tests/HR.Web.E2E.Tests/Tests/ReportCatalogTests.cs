using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class ReportCatalogTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";
    private const string MarcusEmail = "marcus.diallo@acme.example";

    [Fact]
    public async Task CatalogPage_HrCategoryHeading_RendersAsUppercaseHR()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var catalog = new ReportCatalogPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await catalog.GoToAsync(AcmeId);

        Assert.True(await _page.Locator("h5").GetByText("HR", new() { Exact = true }).IsVisibleAsync(),
            "Expected an exact 'HR' category heading");
        Assert.False(await _page.Locator("h5").GetByText("Hr", new() { Exact = true }).IsVisibleAsync(),
            "Did not expect the raw enum name 'Hr' as a category heading");
    }

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

        Assert.True(await catalog.IsCardClickableAsync("HR Headcount Summary"),
            "Expected the HR Headcount Summary card to be clickable (no 'Coming soon' badge) now that its report page exists");
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

        if (await catalog.IsFavouritedAsync("HR Headcount Summary"))
            await catalog.ClickFavouriteAsync("HR Headcount Summary");
        Assert.False(await catalog.IsFavouritedAsync("HR Headcount Summary"));

        try
        {
            await catalog.ClickFavouriteAsync("HR Headcount Summary");
            Assert.True(await catalog.IsFavouritedAsync("HR Headcount Summary"));

            var titlesAfterFavouriting = await catalog.GetCardTitlesInCategoryAsync("Hr");
            Assert.Equal("HR Headcount Summary", titlesAfterFavouriting.FirstOrDefault());

            await _page.ReloadAsync();
            await _page.WaitForSelectorAsync(".report-catalog-card, .hr-empty-state", new() { Timeout = 20_000 });

            Assert.True(await catalog.IsFavouritedAsync("HR Headcount Summary"),
                "Expected the favourite to survive a page reload");

            var titlesAfterReload = await catalog.GetCardTitlesInCategoryAsync("Hr");
            Assert.Equal("HR Headcount Summary", titlesAfterReload.FirstOrDefault());
        }
        finally
        {
            await catalog.GoToAsync(AcmeId);
            if (await catalog.IsFavouritedAsync("HR Headcount Summary"))
                await catalog.ClickFavouriteAsync("HR Headcount Summary");
        }
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

    // Recruitment-category entries ("Recruitment Pipeline Report", "Vacancy Performance Report")
    // are deliberately excluded from this Theory: their catalog visibility is gated by the
    // "reporting:view-recruitment" policy (Recruiter-only — see IdentityModule.AddPolicy), so an
    // HR Administrator like Laura never sees those two cards. They're covered separately by
    // NewRecruitmentReportCard_IsClickable_AndNavigatesToCorrectRoute below, logged in as a
    // Recruiter instead.
    [Theory]
    [InlineData("Employee Starter Report", "employee-starters")]
    [InlineData("Employee Leaver Report", "employee-leavers")]
    [InlineData("Leave Summary Report", "leave-summary")]
    [InlineData("Leave Calendar Export", "leave-calendar")]
    [InlineData("Sickness Report", "sickness")]
    [InlineData("Probation Report", "probation")]
    [InlineData("Onboarding Progress Report", "onboarding-progress")]
    [InlineData("Offboarding Progress Report", "offboarding-progress")]
    [InlineData("Document Compliance Report", "document-compliance")]
    [InlineData("Company Document Acknowledgement Report", "document-acknowledgement")]
    [InlineData("HR Headcount Summary", "hr-headcount-summary")]
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

    [Theory]
    [InlineData("Recruitment Pipeline Report", "recruitment-pipeline")]
    [InlineData("Vacancy Performance Report", "vacancy-performance")]
    [InlineData("Recruitment Pipeline Summary", "recruitment-pipeline-summary")]
    public async Task NewRecruitmentReportCard_IsClickable_AndNavigatesToCorrectRoute(string cardTitleFragment, string routeSlug)
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var catalog = new ReportCatalogPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await catalog.GoToAsync(AcmeId);

        Assert.True(await catalog.HasCardAsync(cardTitleFragment),
            $"Expected the {cardTitleFragment} catalog card to be visible for a Recruiter");
        Assert.True(await catalog.IsCardClickableAsync(cardTitleFragment),
            $"Expected the {cardTitleFragment} card to be clickable (no 'Coming soon' badge)");

        await catalog.ClickCardAsync(cardTitleFragment);

        await _page.WaitForURLAsync($"**/reporting/{routeSlug}", new() { Timeout = 15_000 });
    }

    [Fact]
    public async Task FavouritingNewReportCard_PersistsAcrossNavigationAwayAndBack()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var catalog = new ReportCatalogPage(_page, _fixture.WebBaseUrl);
        var report = new EmployeeStarterReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await catalog.GoToAsync(AcmeId);

        if (await catalog.IsFavouritedAsync("Employee Starter Report"))
            await catalog.ClickFavouriteAsync("Employee Starter Report");
        Assert.False(await catalog.IsFavouritedAsync("Employee Starter Report"));

        try
        {
            await catalog.ClickFavouriteAsync("Employee Starter Report");
            Assert.True(await catalog.IsFavouritedAsync("Employee Starter Report"));

            await catalog.ClickCardAsync("Employee Starter Report");
            await _page.WaitForURLAsync("**/reporting/employee-starters", new() { Timeout = 15_000 });
            Assert.False(await report.HasLoadErrorAsync());

            await catalog.GoToAsync(AcmeId);

            Assert.True(await catalog.IsFavouritedAsync("Employee Starter Report"),
                "Expected the favourite to survive navigating away to the report page and back");
        }
        finally
        {
            await catalog.GoToAsync(AcmeId);
            if (await catalog.IsFavouritedAsync("Employee Starter Report"))
                await catalog.ClickFavouriteAsync("Employee Starter Report");
        }

        Assert.False(await catalog.IsFavouritedAsync("Employee Starter Report"));
    }
}
