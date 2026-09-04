using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the anonymous, workforce-wide Equality &amp; Diversity aggregate report
/// (/companies/{companyId}/reporting/equality-diversity — EqualityDiversityReportPage.razor),
/// reachable from the report catalog as the "Equality &amp; Diversity Report" card and gated by
/// the "reporting:view-equality" permission (HR Administrator).
///
/// This is NOT the per-employee self-service tab — that is covered by EqualityDiversityTabTests
/// and is left untouched here.
///
/// The load-bearing behaviour under test is that the report exposes aggregates ONLY: no
/// drill-through, no links out to individuals, and clicking a table row / dimension card never
/// navigates away from the report route. That is what stops individual monitoring answers being
/// reconstructed from the report.
/// </summary>
public sealed class EqualityDiversityReportTests(HrAdminPersonaFixture fixture)
    : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example"; // HR Administrator
    private const string MarcusEmail = "marcus.diallo@acme.example"; // Recruiter — no HrAdministrator role

    [Fact]
    public async Task Page_Loads_ForHrAdmin_WithSummaryCardsAndAtLeastOneDimensionChart()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new EqualityDiversityReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await report.GoToAsync(AcmeId);

        Assert.False(await report.HasLoadErrorAsync(),
            "Expected the equality & diversity report to load without an error banner for an HR Administrator");
        Assert.True(await report.IsContainerVisibleAsync(),
            "Expected the equality-diversity-report container to be visible");

        Assert.True(await report.AllSummaryCardsVisibleAsync(),
            "Expected the total / respondents / reporting-date summary cards to all be visible");

        Assert.False(string.IsNullOrWhiteSpace(await report.GetTotalTextAsync()),
            "Expected the 'Employees in scope' summary card to show a non-empty value");
        Assert.False(string.IsNullOrWhiteSpace(await report.GetRespondentsTextAsync()),
            "Expected the 'Provided monitoring information' summary card to show a non-empty value");

        var dimensionKeys = await report.GetRenderedDimensionKeysAsync();
        Assert.NotEmpty(dimensionKeys);

        Assert.True(await report.HasAnyChartRenderedAsync(),
            "Expected at least one dimension card to render a chart");
    }

    [Fact]
    public async Task ReportingDateCard_ShowsADate()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new EqualityDiversityReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await report.GoToAsync(AcmeId);

        var dateText = await report.GetReportingDateTextAsync();

        // The page formats ReportingDate as "d MMM yyyy" (e.g. "4 Sep 2026").
        Assert.Matches(new Regex(@"\d{1,2}\s+[A-Za-z]{3,}\s+\d{4}"), dateText);
    }

    /// <summary>
    /// CRITICAL: the report must not offer any drill-through to individuals. Assert there are no
    /// anchor elements anywhere in the report container (and specifically none inside the dimension
    /// cards / their tables), and that clicking a table row and a dimension card does not navigate
    /// away from the report route.
    /// </summary>
    [Fact]
    public async Task Report_HasNoDrillThrough_NoLinks_AndRowsDoNotNavigateAway()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new EqualityDiversityReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await report.GoToAsync(AcmeId);
        Assert.False(await report.HasLoadErrorAsync());

        var expectedRoute = report.RouteFor(AcmeId);

        Assert.Equal(0, await report.GetDimensionAnchorCountAsync());
        Assert.Equal(0, await report.GetAnchorCountAsync());

        var dimensionKeys = await report.GetRenderedDimensionKeysAsync();
        Assert.NotEmpty(dimensionKeys);

        foreach (var key in dimensionKeys)
        {
            var urlAfterRowClick = await report.ClickFirstTableRowAndGetUrlAsync(key);
            Assert.StartsWith(expectedRoute, urlAfterRowClick);

            var urlAfterCardClick = await report.ClickDimensionCardAndGetUrlAsync(key);
            Assert.StartsWith(expectedRoute, urlAfterCardClick);
        }

        Assert.False(await report.HasLoadErrorAsync(),
            "Expected the report to still be rendered (not errored or navigated) after clicking rows/cards");
        Assert.True(await report.IsContainerVisibleAsync(),
            "Expected to still be on the equality & diversity report after clicking rows/cards");
    }

    [Fact]
    public async Task ClickingCatalogCard_NavigatesToReport_ForHrAdmin()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var catalog = new ReportCatalogPage(_page, _fixture.WebBaseUrl);
        var report = new EqualityDiversityReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await catalog.GoToAsync(AcmeId);

        Assert.True(await catalog.HasCardAsync("Equality & Diversity Report"),
            "Expected the Equality & Diversity Report catalog card to be visible for an HR Administrator");
        Assert.True(await catalog.IsCardClickableAsync("Equality & Diversity Report"),
            "Expected the Equality & Diversity Report card to be clickable (no 'Coming soon' badge)");

        await catalog.ClickCardAsync("Equality & Diversity Report");

        await _page.WaitForURLAsync("**/reporting/equality-diversity", new() { Timeout = 15_000 });
        Assert.False(await report.HasLoadErrorAsync());
    }

    [Fact]
    public async Task NonHrPersona_DoesNotSeeCard_InCatalog()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var catalog = new ReportCatalogPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await catalog.GoToAsync(AcmeId);

        // Marcus (Recruiter) does not hold "reporting:view-equality"; the catalog endpoint filters
        // the "Hr"-category entry out server-side — same pattern as
        // HrHeadcountSummaryReportTests.NonHrPersona_DoesNotSeeCard_InCatalog.
        Assert.False(await catalog.HasCardAsync("Equality & Diversity Report"),
            "Expected a non-HR-admin persona to not see the Equality & Diversity Report catalog card");
    }

    [Fact]
    public async Task NonHrPersona_DirectlyNavigatingToReportPage_IsRedirectedToAccessDenied()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var accessDenied = new AccessDeniedPage(_page, _fixture.WebBaseUrl);
        var report = new EqualityDiversityReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        // EqualityDiversityReportPage guards on Session.CanViewEqualityReports via
        // AppSession.GuardAccess, which redirects a persona lacking it to /access-denied (replace)
        // rather than rendering the page. Same guard pattern as HrHeadcountSummaryReportPage.
        await _page.GotoAsync(report.RouteFor(AcmeId));

        await accessDenied.WaitForLoadedAsync();
        Assert.True(accessDenied.IsOnRoute, $"Expected redirect to /access-denied, was: {_page.Url}");
    }
}
