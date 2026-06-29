using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the Upcoming Probation Reviews dashboard widget.
///
/// Depends on the seeded "Carlos Rivera" probation record (company: Acme) which has
/// a pending ManagerCheckIn review — this review should appear in the widget.
/// </summary>
[Collection("E2E")]
public sealed class UpcomingProbationReviewsWidgetTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task Dashboard_ShowsUpcomingProbationReviewsWidget()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new DashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await dashboard.GoToAsync();

        Assert.True(await dashboard.HasUpcomingProbationWidgetAsync(),
            "Expected the 'Upcoming Probation Reviews' widget to be visible on the dashboard");
    }

    [Fact]
    public async Task Dashboard_UpcomingProbationWidget_ShowsCarlosRivera()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new DashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await dashboard.GoToAsync();

        var names = await dashboard.GetUpcomingProbationEmployeeNamesAsync();

        Assert.True(
            names.Any(n => n.Contains("Carlos", StringComparison.OrdinalIgnoreCase)),
            $"Expected 'Carlos Rivera' to appear in the upcoming probation reviews widget. " +
            $"Names found: [{string.Join(", ", names)}]");
    }
}
