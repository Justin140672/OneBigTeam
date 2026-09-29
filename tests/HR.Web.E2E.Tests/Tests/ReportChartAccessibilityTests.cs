using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class ReportChartAccessibilityTests(HrAdminPersonaFixture fixture)
    : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private const string LauraEmail = "laura.bennett@acme.example";
    private const string MarcusEmail = "marcus.diallo@acme.example";

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

        var charts = _page.Locator(".e-chart, .e-accumulationchart, canvas.e-chart");
        var count = await charts.CountAsync();

        for (var i = 0; i < count; i++)
        {
            var chart = charts.Nth(i);

            var hasAriaLabel = !string.IsNullOrWhiteSpace(await chart.GetAttributeAsync("aria-label"));

            var hasTableAlternative =
                await _page.Locator("details:has(table), table.visually-hidden, .visually-hidden table").CountAsync() > 0;

            Assert.True(hasAriaLabel || hasTableAlternative,
                $"Report chart #{i} on '{reportSlug}' has no text alternative (no aria-label and no <details>/visually-hidden data table).");
        }
    }
}
