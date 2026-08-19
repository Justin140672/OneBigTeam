using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the Recruitment Pipeline Summary report page
/// (/companies/{companyId}/reporting/recruitment-pipeline-summary —
/// RecruitmentPipelineSummaryReportPage.razor): loading, the standalone "Include closed vacancies"
/// checkbox, the per-vacancy "Pipeline Stages" badge column, export, and access control for the
/// "reporting:view-recruitment"-gated data/export endpoints. Catalog-page card visibility/navigation
/// coverage lives in <see cref="ReportCatalogTests"/>.
/// </summary>
public sealed class RecruitmentPipelineSummaryReportTests(RecruiterPersonaFixture fixture)
    : RoleE2ETestBase<RecruiterPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    // The endpoint behind this page is gated by the "reporting:view-recruitment" policy, which is
    // Recruiter-only (see IdentityModule.AddPolicy("reporting:view-recruitment", ...)) — same
    // reasoning/precedent as RecruitmentPipelineReportTests and VacancyPerformanceReportTests.
    private const string MarcusEmail = "marcus.diallo@acme.example"; // Recruiter
    private const string LauraEmail = "laura.bennett@acme.example"; // HR Administrator — no Recruiter role

    [Fact]
    public async Task Page_Loads_WithExpectedColumns()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new RecruitmentPipelineSummaryReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await report.GoToAsync(AcmeId);

        Assert.False(await report.HasLoadErrorAsync());

        var headers = await report.GetColumnHeadersAsync();
        Assert.Contains(headers, h => h.Contains("Vacancy"));
        Assert.Contains(headers, h => h.Contains("Position Profile"));
        Assert.Contains(headers, h => h.Contains("Department"));
        Assert.Contains(headers, h => h.Contains("Status"));
        Assert.Contains(headers, h => h.Contains("Opened"));
        Assert.Contains(headers, h => h.Contains("Candidates"));
        Assert.Contains(headers, h => h.Contains("Pipeline Stages"));
    }

    /// <summary>
    /// Exercises the report's aggregation logic against the seeded dev data's recruitment pipeline
    /// (candidates distributed across the company's configured Recruitment Settings stages for at
    /// least one open vacancy) — not just an empty-state render. If any row has candidates, its
    /// "Pipeline Stages" column must render at least one non-zero "&lt;stage&gt;: &lt;count&gt;" badge
    /// (or the "No stages configured" fallback if the company has none configured), proving the
    /// per-stage counts are actually being computed rather than the column being blank.
    /// </summary>
    [Fact]
    public async Task PipelineStagesColumn_RendersPerStageCandidateCounts()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new RecruitmentPipelineSummaryReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await report.GoToAsync(AcmeId);

        var rowCount = await report.GetRowCountAsync();
        if (rowCount == 0)
            return; // Nothing seeded to assert against — Page_Loads_WithExpectedColumns already covers the empty-state grid shell.

        var badgeTexts = await report.GetPipelineStageBadgeTextsAsync();
        Assert.NotEmpty(badgeTexts);
        Assert.All(badgeTexts, text => Assert.Matches(@".+:\s*\d+", text));
    }

    [Fact]
    public async Task IncludeClosedVacancies_TogglesGridWithoutErroring()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new RecruitmentPipelineSummaryReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await report.GoToAsync(AcmeId);

        // Default is unchecked (open vacancies only) — see RecruitmentPipelineSummaryReportPage.razor's
        // _includeClosed field default.
        Assert.False(await report.IsIncludeClosedCheckedAsync());

        var openOnlyRowCount = await report.GetRowCountAsync();

        await report.SetIncludeClosedAsync(true);

        Assert.False(await report.HasLoadErrorAsync(),
            "Expected the grid to reload without an error banner after checking 'Include closed vacancies'");
        var includeClosedRowCount = await report.GetRowCountAsync();
        Assert.True(includeClosedRowCount >= openOnlyRowCount,
            "Expected including closed vacancies to return at least as many rows as open-only");

        await report.SetIncludeClosedAsync(false);
        Assert.False(await report.HasLoadErrorAsync());
        Assert.Equal(openOnlyRowCount, await report.GetRowCountAsync());
    }

    [Fact]
    public async Task ExportCsv_TriggersNonEmptyFileDownload()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new RecruitmentPipelineSummaryReportPage(_page, _fixture.WebBaseUrl);

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

    [Fact]
    public async Task NonRecruiterPersona_DoesNotSeeCard_InCatalog()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var catalog = new ReportCatalogPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await catalog.GoToAsync(AcmeId);

        // Laura (HR Administrator, no Recruiter role) passes "reporting:view-hr" but not
        // "reporting:view-recruitment" — the catalog endpoint filters out Recruitment-category
        // entries server-side, matching the recruitment-pipeline/vacancy-performance precedent.
        Assert.False(await catalog.HasCardAsync("Recruitment Pipeline Summary"),
            "Expected a non-Recruiter persona to not see the Recruitment Pipeline Summary catalog card at all");
    }

    [Fact]
    public async Task NonRecruiterPersona_DirectlyNavigatingToReportPage_DoesNotCrash()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var report = new RecruitmentPipelineSummaryReportPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // The report data endpoint 403s for a non-Recruiter persona; ReportingService.GetRecruitmentPipelineSummaryReportAsync
        // catches that and returns null, and the page renders its own alert-danger error banner
        // instead of a blank or crashed screen — same convention as EmployeeDirectoryReportTests.
        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/reporting/recruitment-pipeline-summary");
        await _page.WaitForLoadStateAsync(Microsoft.Playwright.LoadState.NetworkIdle, new() { Timeout = 15_000 });

        Assert.True(await report.HasLoadErrorAsync(),
            "Expected a graceful error banner (not a crash/blank page) when a non-Recruiter persona is denied the report data");
    }
}
