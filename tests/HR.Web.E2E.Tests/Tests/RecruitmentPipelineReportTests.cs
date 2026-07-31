using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the Recruitment Pipeline report page
/// (/companies/{companyId}/reporting/recruitment-pipeline — RecruitmentPipelineReportPage.razor):
/// loading, the date-range filter panel, the standalone Group by (Recruiter/Vacancy) control,
/// and export. Catalog-page navigation coverage lives in <see cref="ReportCatalogTests"/>.
/// </summary>
[Collection("E2E")]
public sealed class RecruitmentPipelineReportTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    // The endpoint behind this page is gated by the "reporting:view-recruitment" policy, which is
    // Recruiter-only (see IdentityModule.AddPolicy("reporting:view-recruitment", ...) — deliberately
    // non-overlapping with HrAdministrator, same precedent as recruitment:manage/candidate:view).
    // Laura Bennett (HR Administrator, no Recruiter role) would get 403 Forbidden here and the page
    // would show its generic load-error banner — use the Recruiter persona, matching every other
    // Recruitment-domain E2E test (e.g. EmployeeDirectoryReportTests, RecruitmentDashboardTests).
    private const string MarcusEmail = "marcus.diallo@acme.example"; // Recruiter

    [Fact]
    public async Task Page_Loads_WithExpectedColumns()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new RecruitmentPipelineReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await report.GoToAsync(AcmeId);

        Assert.False(await report.HasLoadErrorAsync());

        var headers = await report.GetColumnHeadersAsync();
        Assert.Contains(headers, h => h.Contains("Recruiter") || h.Contains("Vacancy"));
        Assert.Contains(headers, h => h.Contains("Vacancies"));
        Assert.Contains(headers, h => h.Contains("Applicants"));
        Assert.Contains(headers, h => h.Contains("Interviews"));
        Assert.Contains(headers, h => h.Contains("Offers"));
        Assert.Contains(headers, h => h.Contains("Hires"));
    }

    [Fact]
    public async Task DateRangeFilter_ReloadsGridWithoutErroring()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new RecruitmentPipelineReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await report.GoToAsync(AcmeId);

        var unfilteredRowCount = await report.GetRowCountAsync();

        await report.FillDateRangeStartAsync("01/01/2020");
        await report.FillDateRangeEndAsync("31/12/2020");
        await report.ApplyFiltersAsync();

        Assert.False(await report.HasLoadErrorAsync(),
            "Expected the grid to reload without an error banner after applying a date range filter");
        var filteredRowCount = await report.GetRowCountAsync();
        Assert.True(filteredRowCount <= unfilteredRowCount,
            "Expected filtering by date range to return no more rows than the unfiltered set");

        await report.ClearFiltersAsync();
        Assert.False(await report.HasLoadErrorAsync());
    }

    [Fact]
    public async Task GroupByVacancy_ReloadsGridWithoutErroring()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new RecruitmentPipelineReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await report.GoToAsync(AcmeId);

        await report.SelectGroupByAsync("Vacancy");

        Assert.False(await report.HasLoadErrorAsync(),
            "Expected the grid to reload without an error banner after changing Group by to Vacancy");

        var headers = await report.GetColumnHeadersAsync();
        Assert.Contains(headers, h => h.Contains("Vacancy"));
    }

    [Fact]
    public async Task ExportCsv_TriggersNonEmptyFileDownload()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new RecruitmentPipelineReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

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
