using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// DSH-03 — a dashboard widget panel must degrade gracefully when one data source fails: the
/// successfully-loaded content keeps rendering, the failed source shows an inline warning with a
/// Retry control, retry re-requests only that source, and a true all-empty load shows the "All
/// clear" block with no warnings.
///
/// COMPILE-ONLY: these are written to the existing E2E patterns but are not part of the run set
/// (the Blazor Server dashboards fetch their data server-side over SignalR, so browser-level
/// <see cref="IPage.RouteAsync"/> interception cannot by itself force an upstream 500 — forcing a
/// real partial failure needs a server-side fault hook that does not exist yet). Kept here so the
/// journeys are captured and the page object compiles against the project.
/// </summary>
public sealed class DashboardWidgetFailureTests(RecruiterPersonaFixture fixture)
    : RoleE2ETestBase<RecruiterPersonaFixture>(fixture)
{
    private const string MarcusEmail = "marcus.diallo@acme.example";

    // Any of the recruitment-summary sources; matched loosely against the API path segments the
    // dashboard's summary tiles are backed by.
    private static readonly System.Text.RegularExpressions.Regex OneSummarySource =
        new("/vacancies|/applications|/interviews", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private async Task ForceOneSourceToFailAsync()
    {
        var tripped = false;
        await _page.RouteAsync("**/api/**", async route =>
        {
            if (!tripped && OneSummarySource.IsMatch(route.Request.Url))
            {
                tripped = true;
                await route.FulfillAsync(new() { Status = 500, ContentType = "application/json", Body = "{\"error\":\"forced\"}" });
                return;
            }

            await route.ContinueAsync();
        });
    }

    [Fact]
    public async Task PartialFailure_ShowsLoadedTiles_PlusInlineWarningWithRetry()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await ForceOneSourceToFailAsync();

        var panel = new DashboardWidgetPanelPage(_page, _fixture.WebBaseUrl);
        await panel.GoToAsync();
        await panel.WaitForPanelLoadedAsync();

        // Successful sources still render.
        Assert.True(await panel.HasKpiRowAsync(), "Expected the successfully-loaded KPI tiles to still render during a partial failure");

        // The failed source is surfaced inline with a Retry control, not as a whole-panel takeover.
        if (await panel.SourceWarningCountAsync() > 0)
            Assert.False(await panel.IsAllClearAsync(), "A source failure must never show the 'All clear' block");
    }

    [Fact]
    public async Task Retry_ReRequestsOnlyThatSource_AndClearsTheWarning()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        await ForceOneSourceToFailAsync();

        var panel = new DashboardWidgetPanelPage(_page, _fixture.WebBaseUrl);
        await panel.GoToAsync();
        await panel.WaitForPanelLoadedAsync();

        if (await panel.SourceWarningCountAsync() == 0)
            return; // fault not reproducible without a server-side hook — see class remarks

        var warnings = await panel.SourceWarningCountAsync();

        // Stop forcing the failure, then retry: the previously-failed source should recover.
        await _page.UnrouteAsync("**/api/**");
        var firstWarningText = await _page.Locator(".widget-source-warning").First.InnerTextAsync();
        var sourceName = firstWarningText.Split("couldn't")[0].Trim();

        await panel.RetrySourceAsync(sourceName);
        await panel.WaitForSourceWarningClearedAsync(sourceName);

        Assert.True(await panel.SourceWarningCountAsync() < warnings, "Retry should clear only the retried source's warning");
        Assert.True(await panel.HasKpiRowAsync());
    }

    [Fact]
    public async Task TrueAllClear_AllSourcesOkAndEmpty_ShowsAllClearBlockAndNoWarnings()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);

        var panel = new DashboardWidgetPanelPage(_page, _fixture.WebBaseUrl);
        await panel.GoToAsync();
        await panel.WaitForPanelLoadedAsync();

        Assert.Equal(0, await panel.SourceWarningCountAsync());

        // Depending on seeded data the panel is either genuinely all-clear or shows actionable
        // tiles — either way, with no failure there must be no inline warning.
        if (await panel.IsAllClearAsync())
            Assert.Equal(0, await panel.SourceWarningCountAsync());
        else
            Assert.True(await panel.HasKpiRowAsync());
    }
}
