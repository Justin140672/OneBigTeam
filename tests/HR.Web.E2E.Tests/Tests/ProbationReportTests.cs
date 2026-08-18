using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the Probation report page
/// (/companies/{companyId}/reporting/probation — ProbationReportPage.razor): loading (grid and
/// summary stat cards — this page has no ReportFilterPanel or group-by control, only the five
/// summary cards above the grid), and export. Catalog-page navigation coverage lives in
/// <see cref="ReportCatalogTests"/>.
/// </summary>
public sealed class ProbationReportTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example"; // HR Administrator

    [Fact]
    public async Task Page_Loads_WithSummaryCardsAndGridColumns()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new ProbationReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await report.GoToAsync(AcmeId);

        Assert.False(await report.HasLoadErrorAsync());

        // Summary stat cards should each render a non-negative integer (never the -1 parse-failure
        // sentinel), proving the report's aggregate counts loaded successfully.
        Assert.True(await report.GetStatValueAsync("Current Probation") >= 0);
        Assert.True(await report.GetStatValueAsync("Due Reviews") >= 0);
        Assert.True(await report.GetStatValueAsync("Overdue Reviews") >= 0);
        Assert.True(await report.GetStatValueAsync("Passed") >= 0);
        Assert.True(await report.GetStatValueAsync("Extended") >= 0);

        var headers = await report.GetColumnHeadersAsync();
        Assert.Contains(headers, h => h.Contains("Employee"));
        Assert.Contains(headers, h => h.Contains("Status"));
        Assert.Contains(headers, h => h.Contains("Start Date"));
        Assert.Contains(headers, h => h.Contains("Expected End Date"));
        Assert.Contains(headers, h => h.Contains("Due Reviews"));
        Assert.Contains(headers, h => h.Contains("Overdue Reviews"));
    }

    [Fact]
    public async Task ExportCsv_TriggersNonEmptyFileDownload()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new ProbationReportPage(_page, _fixture.WebBaseUrl);

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
