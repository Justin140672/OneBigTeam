using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

public sealed class RecruitmentPipelineSummaryReportTests(RecruiterPersonaFixture fixture)
    : RoleE2ETestBase<RecruiterPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string MarcusEmail = "marcus.diallo@acme.example";
    private const string LauraEmail = "laura.bennett@acme.example";

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
            return;

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

        Assert.False(await catalog.HasCardAsync("Recruitment Pipeline Summary"),
            "Expected a non-Recruiter persona to not see the Recruitment Pipeline Summary catalog card at all");
    }

    [Fact]
    public async Task NonRecruiterPersona_DirectlyNavigatingToReportPage_DoesNotCrash()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var accessDenied = new AccessDeniedPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/reporting/recruitment-pipeline-summary");

        await accessDenied.WaitForLoadedAsync();
        Assert.True(accessDenied.IsOnRoute, $"Expected redirect to /access-denied, was: {_page.Url}");
    }
}
