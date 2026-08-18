using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the Employee Directory report page
/// (/companies/{companyId}/reporting/employee-directory — EmployeeDirectoryReportPage.razor):
/// filter panel, server-side paging, export, and access control for the
/// "reporting:view-hr"-gated data/export endpoints. Catalog-page coverage (search, favourites,
/// navigation into this page) lives in <see cref="ReportCatalogTests"/>.
/// </summary>
public sealed class EmployeeDirectoryReportTests(ParallelBlankPersonaFixture fixture)
    : RoleE2ETestBase<ParallelBlankPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example"; // HR Administrator
    private const string MarcusEmail = "marcus.diallo@acme.example"; // Recruiter — no HrAdministrator role

    [Fact]
    public async Task FilterPanel_ByStatus_ReloadsGridWithoutErroring()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new EmployeeDirectoryReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await report.GoToAsync(AcmeId);

        var unfilteredRowCount = await report.GetRowCountAsync();

        await report.SelectFilterAsync("Status", "Active");
        await report.ApplyFiltersAsync();

        // With seeded dev data we can't assume a specific row-count delta, but the grid must
        // reload cleanly (no crash/error banner) and continue rendering rows/headers.
        Assert.False(await report.HasLoadErrorAsync(), "Expected the grid to reload without an error banner after applying a Status filter");
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
        var report = new EmployeeDirectoryReportPage(_page, _fixture.WebBaseUrl);

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
    public async Task NonHrPersona_DoesNotSeeEmployeeDirectoryCard_InCatalog()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var catalog = new ReportCatalogPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await catalog.GoToAsync(AcmeId);

        // Marcus (Recruiter, no HrAdministrator role) passes "reporting:view-recruitment" but not
        // "reporting:view-hr" — the catalog endpoint itself filters out "Hr"-category entries
        // (including employee-directory) server-side, so there is no card to hide client-side.
        Assert.False(await catalog.HasCardAsync("Employee Directory"),
            "Expected a non-HR-admin persona to not see the Employee Directory catalog card at all");
    }

    [Fact]
    public async Task NonHrPersona_DirectlyNavigatingToReportPage_DoesNotCrash()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new EmployeeDirectoryReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        // The report data endpoint 403s for a non-HR-admin persona; ReportingService.GetEmployeeDirectoryReportAsync
        // catches that and returns null, and the page renders its own alert-danger error banner
        // (see EmployeeDirectoryReportPage.razor's "_error is not null" branch) instead of a blank
        // or crashed screen — mirroring the access-denied convention used elsewhere (see
        // UnauthorizedAccessTests.Employee_CannotAccess_HrInbox).
        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/reporting/employee-directory");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15_000 });

        Assert.True(await report.HasLoadErrorAsync(),
            "Expected a graceful error banner (not a crash/blank page) when a non-HR-admin persona is denied the report data");
    }
}
