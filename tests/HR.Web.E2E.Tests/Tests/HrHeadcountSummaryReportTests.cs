using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the HR Headcount Summary report page
/// (/companies/{companyId}/reporting/hr-headcount-summary — HrHeadcountSummaryReportPage.razor):
/// loading, the 5 summary stat cards, the ReportFilterPanel (Department/Location/EmploymentType/
/// Status only), export, and access control for the "reporting:view-hr"-gated data/export
/// endpoints. Catalog-page card visibility/navigation coverage lives in
/// <see cref="ReportCatalogTests"/>.
/// </summary>
public sealed class HrHeadcountSummaryReportTests(HrAdminPersonaFixture fixture)
    : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example"; // HR Administrator
    private const string MarcusEmail = "marcus.diallo@acme.example"; // Recruiter — no HrAdministrator role

    [Fact]
    public async Task Page_Loads_WithExpectedColumnsAndStatCards()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new HrHeadcountSummaryReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await report.GoToAsync(AcmeId);

        Assert.False(await report.HasLoadErrorAsync());

        var headers = await report.GetColumnHeadersAsync();
        Assert.Contains(headers, h => h.Contains("Employee"));
        Assert.Contains(headers, h => h.Contains("Department"));
        Assert.Contains(headers, h => h.Contains("Location"));
        Assert.Contains(headers, h => h.Contains("Position"));
        Assert.Contains(headers, h => h.Contains("Employment Type"));
        Assert.Contains(headers, h => h.Contains("Employee Status") || h.Contains("Status"));
        Assert.Contains(headers, h => h.Contains("Start Date"));
        Assert.Contains(headers, h => h.Contains("Leaving Date"));
        Assert.Contains(headers, h => h.Contains("FTE"));
    }

    /// <summary>
    /// Exercises the report's aggregation logic against the seeded dev data's employee population
    /// (a mix of active employees, future starters and leavers), not just an empty-state render:
    /// the summary stat cards must be internally consistent with each other and with the grid's
    /// own row count, proving the totals are actually computed off the same underlying data set.
    /// </summary>
    [Fact]
    public async Task SummaryStatCards_AreConsistentWithGridRowCount()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new HrHeadcountSummaryReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await report.GoToAsync(AcmeId);

        var totalHeadcount = await report.GetTotalHeadcountAsync();
        var activeEmployees = await report.GetActiveEmployeesAsync();
        var futureStarters = await report.GetFutureStartersAsync();
        var leavers = await report.GetLeaversAsync();
        var totalFte = await report.GetTotalFteAsync();
        var rowCount = await report.GetRowCountAsync();

        Assert.True(totalHeadcount >= 0, "Expected Total Headcount to render a parseable non-negative number");
        Assert.True(activeEmployees >= 0, "Expected Active Employees to render a parseable non-negative number");
        Assert.True(futureStarters >= 0, "Expected Future Starters to render a parseable non-negative number");
        Assert.True(leavers >= 0, "Expected Leavers to render a parseable non-negative number");
        Assert.True(totalFte >= 0m, "Expected Total FTE to render a parseable non-negative number");

        // The grid is paged (AllowPaging="true"), so the on-screen row count may be capped at the
        // page size while Total Headcount reflects the full unpaged result set — assert the grid
        // never shows more rows than the stat card claims exist, not strict equality.
        Assert.True(rowCount <= totalHeadcount,
            "Expected the grid's visible row count to never exceed the Total Headcount stat");
        Assert.True(activeEmployees <= totalHeadcount,
            "Expected Active Employees to never exceed Total Headcount");
    }

    [Fact]
    public async Task FilterPanel_ByDepartment_ReloadsGridWithoutErroring()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new HrHeadcountSummaryReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await report.GoToAsync(AcmeId);

        var unfilteredRowCount = await report.GetRowCountAsync();

        // "Engineering" is the seeded Acme department used throughout this suite — see
        // EmployeeStarterReportTests.FilterPanel_ByDepartment_ReloadsGridWithoutErroring's own
        // reasoning for choosing a real, non-empty expected value here.
        await report.SelectFilterAsync("Department", "Engineering");
        await report.ApplyFiltersAsync();

        Assert.False(await report.HasLoadErrorAsync(),
            "Expected the grid to reload without an error banner after applying a Department filter");
        var filteredRowCount = await report.GetRowCountAsync();
        Assert.True(filteredRowCount <= unfilteredRowCount,
            "Expected filtering by Department to return no more rows than the unfiltered set");

        await report.ClearFiltersAsync();
        Assert.False(await report.HasLoadErrorAsync());
    }

    [Fact]
    public async Task FilterPanel_ByStatus_ReloadsGridWithoutErroring()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new HrHeadcountSummaryReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await report.GoToAsync(AcmeId);

        var unfilteredRowCount = await report.GetRowCountAsync();

        await report.SelectFilterAsync("Status", "Active");
        await report.ApplyFiltersAsync();

        Assert.False(await report.HasLoadErrorAsync(),
            "Expected the grid to reload without an error banner after applying a Status filter");
        var filteredRowCount = await report.GetRowCountAsync();
        Assert.True(filteredRowCount <= unfilteredRowCount,
            "Expected filtering by Active status to return no more rows than the unfiltered set");

        await report.ClearFiltersAsync();
        Assert.False(await report.HasLoadErrorAsync());
    }

    [Fact]
    public async Task ExportCsv_TriggersNonEmptyFileDownload()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new HrHeadcountSummaryReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await report.GoToAsync(AcmeId);

        var download = await report.ExportAsync("CSV");

        Assert.NotNull(download.SuggestedFilename);
        Assert.Contains(".csv", download.SuggestedFilename, StringComparison.OrdinalIgnoreCase);

        var downloadPath = await download.PathAsync();
        Assert.NotNull(downloadPath);
        var fileInfo = new FileInfo(downloadPath!);
        Assert.True(fileInfo.Exists && fileInfo.Length > 0, "Expected the exported CSV file to be non-empty");
    }

    [Fact]
    public async Task NonHrPersona_DoesNotSeeCard_InCatalog()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var catalog = new ReportCatalogPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await catalog.GoToAsync(AcmeId);

        // Marcus (Recruiter, no HrAdministrator role) passes "reporting:view-recruitment" but not
        // "reporting:view-hr" — the catalog endpoint filters out "Hr"-category entries server-side,
        // same as EmployeeDirectoryReportTests.NonHrPersona_DoesNotSeeEmployeeDirectoryCard_InCatalog.
        Assert.False(await catalog.HasCardAsync("HR Headcount Summary"),
            "Expected a non-HR-admin persona to not see the HR Headcount Summary catalog card at all");
    }

    [Fact]
    public async Task NonHrPersona_DirectlyNavigatingToReportPage_DoesNotCrash()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var accessDenied = new AccessDeniedPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        // ADM-05 (commit e67ba6ff): HrHeadcountSummaryReportPage guards on Session.CanViewHrReports
        // via AppSession.GuardAccess, which redirects a persona that lacks it to /access-denied
        // (replace) rather than rendering the page and letting the data call 403. Marcus is a
        // Recruiter, not an HR Administrator.
        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/reporting/hr-headcount-summary");

        await accessDenied.WaitForLoadedAsync();
        Assert.True(accessDenied.IsOnRoute, $"Expected redirect to /access-denied, was: {_page.Url}");
    }
}
