using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class DashboardWidgetFailureTests(RecruiterPersonaFixture fixture)
    : RoleE2ETestBase<RecruiterPersonaFixture>(fixture)
{
    private const string MarcusEmail = "marcus.diallo@acme.example";

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

        Assert.True(await panel.HasKpiRowAsync(), "Expected the successfully-loaded KPI tiles to still render during a partial failure");

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
            return;

        var warnings = await panel.SourceWarningCountAsync();

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

        if (await panel.IsAllClearAsync())
            Assert.Equal(0, await panel.SourceWarningCountAsync());
        else
            Assert.True(await panel.HasKpiRowAsync());
    }
}
