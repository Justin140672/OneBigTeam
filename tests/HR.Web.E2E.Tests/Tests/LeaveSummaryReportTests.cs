using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the Leave Summary report page
/// (/companies/{companyId}/reporting/leave-summary — LeaveSummaryReportPage.razor): loading
/// (aggregated, non-paged grid), the inline PolicyYear/Department/GroupBy filters, and export.
/// Catalog-page coverage (card visibility/navigation) lives in <see cref="ReportCatalogTests"/>.
/// </summary>
[Collection("E2E")]
public sealed class LeaveSummaryReportTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example"; // HR Administrator

    [Fact]
    public async Task Page_Loads_WithExpectedColumns()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new LeaveSummaryReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await report.GoToAsync(AcmeId);

        Assert.False(await report.HasLoadErrorAsync());

        var headers = await report.GetColumnHeadersAsync();
        Assert.Contains(headers, h => h.Contains("Entitlement Days"));
        Assert.Contains(headers, h => h.Contains("Booked Days"));
        Assert.Contains(headers, h => h.Contains("Approved Days"));
        Assert.Contains(headers, h => h.Contains("Remaining Days"));
        Assert.Contains(headers, h => h.Contains("Pending Requests"));
    }

    [Fact]
    public async Task GroupByChange_ReloadsGridWithoutErroring_AndChangesGroupColumnHeader()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new LeaveSummaryReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await report.GoToAsync(AcmeId);

        // Default GroupBy is "Employee" (see LeaveSummaryReportPage.razor's _groupBy default),
        // so its grouping column header should read "Employee" before any change.
        var headersBefore = await report.GetColumnHeadersAsync();
        Assert.Contains(headersBefore, h => h.Contains("Employee"));

        await report.SelectGroupByAsync("Department");

        Assert.False(await report.HasLoadErrorAsync(),
            "Expected the grid to reload without an error banner after changing Group By");
        var headersAfter = await report.GetColumnHeadersAsync();
        Assert.Contains(headersAfter, h => h.Contains("Department"));
    }

    [Fact]
    public async Task PolicyYearFilter_ReloadsGridWithoutErroring()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new LeaveSummaryReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await report.GoToAsync(AcmeId);

        await report.SetPolicyYearAsync(DateTime.UtcNow.Year - 1);
        await report.ApplyFiltersAsync();

        Assert.False(await report.HasLoadErrorAsync(),
            "Expected the grid to reload without an error banner after changing Policy Year");
    }

    [Fact]
    public async Task ExportCsv_TriggersNonEmptyFileDownload()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new LeaveSummaryReportPage(_page, _fixture.WebBaseUrl);

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
