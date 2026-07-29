using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the Employee Starter report page
/// (/companies/{companyId}/reporting/employee-starters — EmployeeStarterReportPage.razor):
/// loading, the ReportFilterPanel (Department/Location/PositionProfile/EmploymentType/DateRange
/// only — Manager and Status filters are hidden on this page), and export. Catalog-page coverage
/// (card visibility/navigation) lives in <see cref="ReportCatalogTests"/>.
/// </summary>
[Collection("E2E")]
public sealed class EmployeeStarterReportTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example"; // HR Administrator

    [Fact]
    public async Task Page_Loads_WithExpectedColumns()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new EmployeeStarterReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await report.GoToAsync(AcmeId);

        Assert.False(await report.HasLoadErrorAsync());

        var headers = await report.GetColumnHeadersAsync();
        Assert.Contains(headers, h => h.Contains("Name"));
        Assert.Contains(headers, h => h.Contains("Start Date"));
        Assert.Contains(headers, h => h.Contains("Recruiter"));
        Assert.Contains(headers, h => h.Contains("Department"));
        Assert.Contains(headers, h => h.Contains("Position"));
        Assert.Contains(headers, h => h.Contains("Onboarding Status"));
        Assert.Contains(headers, h => h.Contains("Probation Status"));
    }

    [Fact]
    public async Task FilterPanel_ByDepartment_ReloadsGridWithoutErroring()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new EmployeeStarterReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await report.GoToAsync(AcmeId);

        var unfilteredRowCount = await report.GetRowCountAsync();

        // With seeded dev data we can't assume a specific department exists/row-count delta, but
        // the filter panel's own combobox items (all departments in the company) are always safe
        // to pick the first one from — mirrors EmployeeDirectoryReportTests' status-filter smoke
        // check in spirit: reload must not error, and filtering can only narrow the result set.
        await report.SelectFilterAsync("Department", "");
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
    public async Task ExportCsv_TriggersNonEmptyFileDownload()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new EmployeeStarterReportPage(_page, _fixture.WebBaseUrl);

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
}
