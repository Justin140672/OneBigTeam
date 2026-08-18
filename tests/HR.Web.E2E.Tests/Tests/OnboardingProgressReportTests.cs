using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the Onboarding Progress report page
/// (/companies/{companyId}/reporting/onboarding-progress — OnboardingProgressReportPage.razor):
/// loading (summary stat cards and grid columns), the "Overdue only" checkbox filter, and export.
/// Catalog-page navigation coverage lives in <see cref="ReportCatalogTests"/>.
/// </summary>
public sealed class OnboardingProgressReportTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example"; // HR Administrator

    [Fact]
    public async Task Page_Loads_WithSummaryCardsAndGridColumns()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new OnboardingProgressReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await report.GoToAsync(AcmeId);

        Assert.False(await report.HasLoadErrorAsync());

        // Summary stat cards should each render a non-negative integer (never the -1 parse-failure
        // sentinel), proving the report's aggregate counts loaded successfully.
        Assert.True(await report.GetStatValueAsync("Total Employees") >= 0);
        Assert.True(await report.GetStatValueAsync("Total Outstanding Tasks") >= 0);
        Assert.True(await report.GetStatValueAsync("Overdue Employees") >= 0);

        var headers = await report.GetColumnHeadersAsync();
        Assert.Contains(headers, h => h.Contains("Employee"));
        Assert.Contains(headers, h => h.Contains("Plan Status"));
        Assert.Contains(headers, h => h.Contains("Progress"));
        Assert.Contains(headers, h => h.Contains("Outstanding Tasks"));
        Assert.Contains(headers, h => h.Contains("Has Overdue"));
    }

    [Fact]
    public async Task OverdueOnlyFilter_ReloadsGridWithoutErroring()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new OnboardingProgressReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await report.GoToAsync(AcmeId);

        var rowCountBefore = await report.GetRowCountAsync();

        await report.SetOverdueOnlyAsync(true);
        await report.ApplyAsync();

        Assert.False(await report.HasLoadErrorAsync(),
            "Expected the grid to reload without an error banner after applying the Overdue only filter");

        // Overdue-only can only narrow (or leave unchanged) the set of employees shown, never grow it.
        var rowCountAfter = await report.GetRowCountAsync();
        Assert.True(rowCountAfter <= rowCountBefore,
            "Expected the Overdue only filter to narrow (or leave unchanged) the row count");
    }

    [Fact]
    public async Task ExportCsv_TriggersNonEmptyFileDownload()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new OnboardingProgressReportPage(_page, _fixture.WebBaseUrl);

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
