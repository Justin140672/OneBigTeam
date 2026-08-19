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
///
/// Runs serialized against HrDashboardTests (HrFavouritesSerialTestBase) — both toggle Laura
/// Bennett's shared, server-persisted report favourites. See GroupSerializedTestBases.cs.
/// </summary>
public sealed class ReportCatalogTests(HrAdminPersonaFixture fixture) : HrFavouritesSerialTestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example"; // HR Administrator
    private const string MarcusEmail = "marcus.diallo@acme.example"; // Recruiter

    /// <summary>
    /// The category heading for ReportCategory.Hr must render as "HR" (correct capitalisation),
    /// not the raw PascalCase enum name "Hr" — see ReportCatalogPage.razor's CategoryLabel mapping.
    /// </summary>
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

        // HR Headcount Summary was a phase-1 "Coming soon" placeholder card but is now a fully
        // built, clickable report (see ReportRoutes.Map in ReportingModels.cs) — its clickability
        // and navigation are covered by NewReportCard_IsClickable_AndNavigatesToCorrectRoute below.
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

        // "HR Headcount Summary" and "Employee Directory" are both in the "Hr" category, with
        // "Employee Directory" sorting first alphabetically by default (E < H) — favouriting
        // "HR Headcount Summary" should move it ahead of "Employee Directory".
        //
        // Self-heal rather than assert-and-fail: if an earlier run's assertion failure ever left
        // this favourited despite the try/finally below, asserting False unconditionally would
        // fail every subsequent run forever with no way to recover. Clear any pre-existing
        // favourite first so this test is self-repairing against the shared, long-lived E2E dev
        // database.
        if (await catalog.IsFavouritedAsync("HR Headcount Summary"))
            await catalog.ClickFavouriteAsync("HR Headcount Summary");
        Assert.False(await catalog.IsFavouritedAsync("HR Headcount Summary"));

        try
        {
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
        finally
        {
            // Leaves LauraEmail's favourites clean for other tests relying on the seeded dev
            // database — e.g. HrDashboardTests.FavouriteReportsWidget_ShowsEmptyState_WhenNothingFavourited
            // asserts this persona has no favourites at all, same convention as
            // HrDashboardTests.FavouriteReportsWidget_ShowsFavouritedReport_AndNavigatesToItOnClick's
            // own cleanup.
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

        // Self-heal rather than assert-and-fail here: if an earlier run's assertion failure ever
        // slipped past the try/finally below (or a completely different bug left this favourited),
        // asserting False unconditionally would fail every single subsequent run forever with no
        // way to recover, since the very check that should trigger cleanup would itself be the
        // thing failing. Clear any pre-existing favourite first so this test is self-repairing
        // against the shared, long-lived E2E dev database.
        if (await catalog.IsFavouritedAsync("Employee Starter Report"))
            await catalog.ClickFavouriteAsync("Employee Starter Report");
        Assert.False(await catalog.IsFavouritedAsync("Employee Starter Report"));

        // Guard the mutating middle section with try/finally so an assertion failure here still
        // un-favourites the report before the test exits — without this, a failed assertion (e.g.
        // the "survived navigation" check below) leaves "Employee Starter Report" permanently
        // favourited on the shared, long-lived E2E dev database, which then pollutes every other
        // test that reads Laura Bennett's favourites (HrDashboardTests.FavouriteReportsWidget_
        // ShowsEmptyState_WhenNothingFavourited and ...ShowsFavouritedReport_AndNavigatesToItOnClick
        // in particular — both assume "Employee Starter Report" starts unfavourited). Same pattern
        // as FavouriteToggle_PersistsAcrossReload_AndSortsFirstInCategory above.
        try
        {
            await catalog.ClickFavouriteAsync("Employee Starter Report");
            Assert.True(await catalog.IsFavouritedAsync("Employee Starter Report"));

            // Navigate away into the report page itself, then back to the catalog — proves the
            // favourite round-tripped through the server rather than only surviving in the same
            // component instance's in-memory state.
            await catalog.ClickCardAsync("Employee Starter Report");
            await _page.WaitForURLAsync("**/reporting/employee-starters", new() { Timeout = 15_000 });
            Assert.False(await report.HasLoadErrorAsync());

            await catalog.GoToAsync(AcmeId);

            Assert.True(await catalog.IsFavouritedAsync("Employee Starter Report"),
                "Expected the favourite to survive navigating away to the report page and back");
        }
        finally
        {
            // Clean up so this test is repeatable against the shared, long-lived E2E dev database.
            // GoToAsync back to the catalog first in case the try block failed before returning here.
            await catalog.GoToAsync(AcmeId);
            if (await catalog.IsFavouritedAsync("Employee Starter Report"))
                await catalog.ClickFavouriteAsync("Employee Starter Report");
        }

        Assert.False(await catalog.IsFavouritedAsync("Employee Starter Report"));
    }
}
