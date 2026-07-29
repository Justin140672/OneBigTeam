using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the Leave Calendar report page
/// (/companies/{companyId}/reporting/leave-calendar — LeaveCalendarReportPage.razor): loading
/// (non-paged grid), the inline Year/Month/Department filters, and export (this report is
/// export-oriented per its ticket — its Export button uses "e-primary" styling rather than the
/// "e-flat" style used elsewhere). Catalog-page coverage (card visibility/navigation) lives in
/// <see cref="ReportCatalogTests"/>.
/// </summary>
[Collection("E2E")]
public sealed class LeaveCalendarReportTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example"; // HR Administrator

    [Fact]
    public async Task Page_Loads_WithExpectedColumns()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new LeaveCalendarReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await report.GoToAsync(AcmeId);

        Assert.False(await report.HasLoadErrorAsync());

        var headers = await report.GetColumnHeadersAsync();
        Assert.Contains(headers, h => h.Contains("Employee"));
        Assert.Contains(headers, h => h.Contains("Department"));
        Assert.Contains(headers, h => h.Contains("Leave Start"));
        Assert.Contains(headers, h => h.Contains("Leave End"));
        Assert.Contains(headers, h => h.Contains("Leave Type"));
        Assert.Contains(headers, h => h.Contains("Duration"));
        Assert.Contains(headers, h => h.Contains("Approval Status"));
    }

    [Fact]
    public async Task MonthFilter_ReloadsGridWithoutErroring()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new LeaveCalendarReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await report.GoToAsync(AcmeId);

        var unfilteredRowCount = await report.GetRowCountAsync();

        await report.SelectMonthAsync("January");
        await report.ApplyFiltersAsync();

        Assert.False(await report.HasLoadErrorAsync(),
            "Expected the grid to reload without an error banner after changing Month");
        var filteredRowCount = await report.GetRowCountAsync();
        Assert.True(filteredRowCount <= unfilteredRowCount || unfilteredRowCount == 0,
            "Expected filtering by Month to return no more rows than the unfiltered set");
    }

    [Fact]
    public async Task ExportCsv_TriggersNonEmptyFileDownload()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new LeaveCalendarReportPage(_page, _fixture.WebBaseUrl);

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
