using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the Offboarding Progress report page
/// (/companies/{companyId}/reporting/offboarding-progress — OffboardingProgressReportPage.razor):
/// loading (summary stat cards and grid columns — this page has no filter control at all) and
/// export. Catalog-page navigation coverage lives in <see cref="ReportCatalogTests"/>.
/// </summary>
[Collection("E2E")]
public sealed class OffboardingProgressReportTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example"; // HR Administrator

    [Fact]
    public async Task Page_Loads_WithSummaryCardsAndGridColumns()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new OffboardingProgressReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await report.GoToAsync(AcmeId);

        Assert.False(await report.HasLoadErrorAsync());

        // Summary stat cards should each render a non-negative integer (never the -1 parse-failure
        // sentinel), proving the report's aggregate counts loaded successfully.
        Assert.True(await report.GetStatValueAsync("Total Employees") >= 0);
        Assert.True(await report.GetStatValueAsync("Outstanding Access") >= 0);
        Assert.True(await report.GetStatValueAsync("Outstanding Assets") >= 0);

        var headers = await report.GetColumnHeadersAsync();
        Assert.Contains(headers, h => h.Contains("Employee"));
        Assert.Contains(headers, h => h.Contains("Last Working Day"));
        Assert.Contains(headers, h => h.Contains("Status"));
        Assert.Contains(headers, h => h.Contains("Outstanding Tasks"));
        Assert.Contains(headers, h => h.Contains("Completed Tasks"));
        Assert.Contains(headers, h => h.Contains("Access Disabled"));
        Assert.Contains(headers, h => h.Contains("Documents Returned"));
        Assert.Contains(headers, h => h.Contains("Assets Returned"));
    }

    [Fact]
    public async Task ExportCsv_TriggersNonEmptyFileDownload()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new OffboardingProgressReportPage(_page, _fixture.WebBaseUrl);

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
