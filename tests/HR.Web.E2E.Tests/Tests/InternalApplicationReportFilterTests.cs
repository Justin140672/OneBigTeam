using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Internal recruitment Ticket 6 — the "Applications" (All applications / Internal / External) filter
/// on the Vacancy Performance and Recruitment Pipeline Summary reports. These tests only prove that
/// each selection is applied (the combobox keeps it through the reload it triggers) and that the
/// grid re-renders without the load-error alert. They deliberately assert no counts: report figures
/// are company-wide and shared with every parallel recruitment test. The deterministic row-level
/// check (own vacancy's Candidates count under each filter) lives in
/// InternalApplicationIdentificationTests.RecruitmentPipelineReport_GroupedByVacancy_*.
///
/// Read-only (creates and mutates nothing), so no serialization gate is needed. Marcus Diallo
/// (Recruiter) holds reporting:view-recruitment.
/// </summary>
public sealed class InternalApplicationReportFilterTests(RecruiterPersonaFixture fixture)
    : RoleE2ETestBase<RecruiterPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string MarcusEmail = "marcus.diallo@acme.example";

    private async Task LoginAsync()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);
    }

    [Fact]
    public async Task VacancyPerformanceReport_ApplicationTypeFilter_SelectionSticksAndGridReloadsWithoutError()
    {
        await LoginAsync();
        var report = new VacancyPerformanceReportPage(_page, _fixture.WebBaseUrl);
        await report.GoToAsync(AcmeId);

        await report.ExpectApplicationTypeAsync(ReportApplicationTypeFilter.AllApplications);
        await report.ExpectRenderedWithoutErrorAsync();

        foreach (var label in new[]
                 {
                     ReportApplicationTypeFilter.Internal,
                     ReportApplicationTypeFilter.External,
                     ReportApplicationTypeFilter.AllApplications,
                 })
        {
            await report.SelectApplicationTypeAsync(label);
            await report.ExpectRenderedWithoutErrorAsync();
            await report.ExpectApplicationTypeAsync(label);
        }
    }

    [Fact]
    public async Task RecruitmentPipelineSummaryReport_ApplicationTypeFilter_SelectionSticksAndGridReloadsWithoutError()
    {
        await LoginAsync();
        var report = new RecruitmentPipelineSummaryReportPage(_page, _fixture.WebBaseUrl);
        await report.GoToAsync(AcmeId);

        await report.ExpectApplicationTypeAsync(ReportApplicationTypeFilter.AllApplications);
        await report.ExpectRenderedWithoutErrorAsync();

        foreach (var label in new[]
                 {
                     ReportApplicationTypeFilter.Internal,
                     ReportApplicationTypeFilter.External,
                     ReportApplicationTypeFilter.AllApplications,
                 })
        {
            await report.SelectApplicationTypeAsync(label);
            await report.ExpectRenderedWithoutErrorAsync();
            await report.ExpectApplicationTypeAsync(label);
        }
    }

    [Fact]
    public async Task RecruitmentPipelineReport_ApplicationTypeFilter_SelectionSticksAndGridReloadsWithoutError()
    {
        await LoginAsync();
        var report = new RecruitmentPipelineReportPage(_page, _fixture.WebBaseUrl);
        await report.GoToAsync(AcmeId);

        await report.ExpectApplicationTypeAsync(ReportApplicationTypeFilter.AllApplications);
        await report.ExpectRenderedWithoutErrorAsync();

        await report.SelectApplicationTypeAsync(ReportApplicationTypeFilter.Internal);
        await report.ExpectRenderedWithoutErrorAsync();
        await report.ExpectApplicationTypeAsync(ReportApplicationTypeFilter.Internal);

        // Changing Group by reloads the grid again; the Applications selection must survive it.
        await report.SelectGroupByAsync("Vacancy");
        await report.ExpectRenderedWithoutErrorAsync();
        await report.ExpectApplicationTypeAsync(ReportApplicationTypeFilter.Internal);
    }
}
