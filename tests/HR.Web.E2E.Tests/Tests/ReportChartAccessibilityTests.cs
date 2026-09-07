using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// NFR-05 §6: DSH-07 covered the dashboard charts' accessible table alternative
/// (see <c>DashboardAccessibilityTests.HrDashboardCharts_ProvideAccessibleTableAlternative</c>).
/// This class extends that check to the reporting pages. At the time of writing the
/// recruitment-pipeline, sickness and vacancy-performance report pages render tabular
/// <c>SfGrid</c> output only — no <c>&lt;SfChart&gt;</c>/<c>&lt;SfAccumulationChart&gt;</c> — so the
/// assertion below (every chart/canvas on the page must have a text alternative) currently passes
/// vacuously. It's in place so that if a chart is ever added to a report page without an
/// accompanying <c>&lt;details&gt;&lt;table&gt;</c> or <c>aria-label</c>/visually-hidden data table,
/// this gate fails.
///
/// NFR-05: chart text alternative — see exceptions register.
/// </summary>
public sealed class ReportChartAccessibilityTests(HrAdminPersonaFixture fixture)
    : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private const string LauraEmail = "laura.bennett@acme.example"; // HR Administrator — no Recruiter role
    private const string MarcusEmail = "marcus.diallo@acme.example"; // Recruiter — no HrAdministrator role

    // The recruitment-pipeline and vacancy-performance report pages guard their data on
    // Session.CanViewRecruitmentReports (permission "reporting:view-recruitment"), which is
    // Recruiter-only — Laura (HR Administrator) does not hold it and would be bounced to
    // /access-denied before the grid ever renders (same precedent as RecruitmentPipelineReportTests
    // and VacancyPerformanceReportTests). The sickness report is gated on CanViewHrReports instead,
    // which Laura does hold.
    private static string LoginEmailFor(string reportSlug) => reportSlug switch
    {
        "recruitment-pipeline" or "vacancy-performance" => MarcusEmail,
        _ => LauraEmail,
    };

    private async Task LoginAsync(string email)
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(email);
    }

    [Theory]
    [InlineData("recruitment-pipeline")]
    [InlineData("sickness")]
    [InlineData("vacancy-performance")]
    public async Task ReportCharts_ProvideTextAlternative(string reportSlug)
    {
        await LoginAsync(LoginEmailFor(reportSlug));
        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/reporting/{reportSlug}");
        await _page.WaitForSelectorAsync(".e-grid .e-row, .e-grid .e-emptyrow", new() { Timeout = 20_000 });

        // Syncfusion charts render an <svg>/<canvas> inside an .e-chart / .e-accumulationchart host.
        var charts = _page.Locator(".e-chart, .e-accumulationchart, canvas.e-chart");
        var count = await charts.CountAsync();

        for (var i = 0; i < count; i++)
        {
            var chart = charts.Nth(i);

            var hasAriaLabel = !string.IsNullOrWhiteSpace(await chart.GetAttributeAsync("aria-label"));

            // A sibling <details><table> alternative, or a visually-hidden data table, anywhere on
            // the page counts as the text alternative (mirrors the dashboard pattern).
            var hasTableAlternative =
                await _page.Locator("details:has(table), table.visually-hidden, .visually-hidden table").CountAsync() > 0;

            Assert.True(hasAriaLabel || hasTableAlternative,
                $"Report chart #{i} on '{reportSlug}' has no text alternative (no aria-label and no <details>/visually-hidden data table).");
        }
    }
}
