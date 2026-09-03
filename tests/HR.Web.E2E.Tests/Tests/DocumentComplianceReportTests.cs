using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the Document Compliance report page
/// (/companies/{companyId}/reporting/document-compliance — DocumentComplianceReportPage.razor):
/// loading (summary stat cards and grid columns), the Position Profile dropdown filter, and
/// export. Catalog-page navigation coverage lives in <see cref="ReportCatalogTests"/>.
/// </summary>
public sealed class DocumentComplianceReportTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example"; // HR Administrator

    // Seeded position profile used elsewhere in the suite (e.g. ApplicationToEmployeeFlowTests),
    // known to exist for Acme.
    private const string SeniorSoftwareEngineerProfile = "Senior Software Engineer";

    [Fact]
    public async Task Page_Loads_WithSummaryCardsAndGridColumns()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new DocumentComplianceReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await report.GoToAsync(AcmeId);

        Assert.False(await report.HasLoadErrorAsync());

        // Summary stat cards should each render a non-negative integer (never the -1 parse-failure
        // sentinel), proving the report's aggregate counts loaded successfully.
        Assert.True(await report.GetStatValueAsync("Total Employees") >= 0);
        Assert.True(await report.GetStatValueAsync("Total Missing") >= 0);
        Assert.True(await report.GetStatValueAsync("Expiring Soon") >= 0);
        Assert.True(await report.GetStatValueAsync("Expired") >= 0);

        var headers = await report.GetColumnHeadersAsync();
        Assert.Contains(headers, h => h.Contains("Employee"));
        Assert.Contains(headers, h => h.Contains("Required"));
        Assert.Contains(headers, h => h.Contains("Uploaded"));
        Assert.Contains(headers, h => h.Contains("Missing"));
        Assert.Contains(headers, h => h.Contains("Expiring Soon"));
        Assert.Contains(headers, h => h.Contains("Expired"));
    }

    [Fact]
    public async Task PositionProfileFilter_ReloadsGridWithoutErroring()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new DocumentComplianceReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await report.GoToAsync(AcmeId);

        var rowCountBefore = await report.GetRowCountAsync();

        await report.SelectPositionProfileAsync(SeniorSoftwareEngineerProfile);
        await report.ApplyFiltersAsync();

        Assert.False(await report.HasLoadErrorAsync(),
            "Expected the grid to reload without an error banner after applying the Position Profile filter");

        // Filtering to a single position profile can only narrow (or leave unchanged) the set of
        // employees shown, never grow it.
        var rowCountAfter = await report.GetRowCountAsync();
        Assert.True(rowCountAfter <= rowCountBefore,
            "Expected the Position Profile filter to narrow (or leave unchanged) the row count");
    }

    [Fact]
    public async Task ExportCsv_TriggersNonEmptyFileDownload()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new DocumentComplianceReportPage(_page, _fixture.WebBaseUrl);

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

    /// <summary>
    /// Selecting "Excel" from the shared ExportMenu (ExportMenu.razor's SfDropDownButton) must
    /// invoke the Syncfusion export action — observed here as a non-empty .xlsx download event.
    /// </summary>
    [Fact]
    public async Task ExportExcel_TriggersNonEmptyFileDownload()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new DocumentComplianceReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await report.GoToAsync(AcmeId);

        var download = await report.ExportAsync("Excel");

        Assert.NotNull(download.SuggestedFilename);
        Assert.Contains(".xls", download.SuggestedFilename, StringComparison.OrdinalIgnoreCase);

        var downloadPath = await download.PathAsync();
        Assert.NotNull(downloadPath);
        Assert.True(new FileInfo(downloadPath!).Length > 0, "Expected the exported Excel file to be non-empty");
    }

    /// <summary>
    /// Selecting "PDF" from the shared ExportMenu must invoke the Syncfusion export action —
    /// observed here as a non-empty .pdf download event.
    /// </summary>
    [Fact]
    public async Task ExportPdf_TriggersNonEmptyFileDownload()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new DocumentComplianceReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await report.GoToAsync(AcmeId);

        var download = await report.ExportAsync("PDF");

        Assert.NotNull(download.SuggestedFilename);
        Assert.Contains(".pdf", download.SuggestedFilename, StringComparison.OrdinalIgnoreCase);

        var downloadPath = await download.PathAsync();
        Assert.NotNull(downloadPath);
        Assert.True(new FileInfo(downloadPath!).Length > 0, "Expected the exported PDF file to be non-empty");
    }
}
