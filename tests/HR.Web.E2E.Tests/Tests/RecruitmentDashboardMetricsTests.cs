using HR.Web.E2E.Tests.Infrastructure;
using Microsoft.Playwright;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

public sealed class RecruitmentDashboardMetricsTests(RecruiterPersonaFixture fixture)
    : RoleE2ETestBase<RecruiterPersonaFixture>(fixture)
{
    private const string MarcusEmail = "marcus.diallo@acme.example";

    private static readonly string[] DrillableTiles =
    [
        "New applications",
        "Candidates in progress",
        "Interviews requiring action",
        "Offers awaiting response",
    ];

    [Fact]
    public async Task Dashboard_ShowsAllSummaryTiles_IncludingCandidatesInProgress_WithNumericValues()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new RecruitmentDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);
        await dashboard.GoToAsync();

        Assert.True(await dashboard.GetSummaryTileValueAsync("Open vacancies") >= 0);
        Assert.True(await dashboard.GetSummaryTileValueAsync("New applications") >= 0);
        Assert.True(await dashboard.GetSummaryTileValueAsync("Candidates in progress") >= 0);
        Assert.True(await dashboard.GetSummaryTileValueAsync("Interviews requiring action") >= 0);
        Assert.True(await dashboard.GetSummaryTileValueAsync("Offers awaiting response") >= 0);
        Assert.True(await dashboard.GetSummaryTileValueAsync("Stale vacancies") >= 0);
    }

    [Fact]
    public async Task OpenVacanciesTile_OpensVacancyList_FilteredToOpenStatus()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new RecruitmentDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);
        await dashboard.GoToAsync();
        var openCount = await dashboard.GetSummaryTileValueAsync("Open vacancies");

        await _page.Locator(".widget-kpi-row[role='list'] .widget-kpi")
            .Filter(new() { HasText = "Open vacancies" }).First.ClickAsync();
        await _page.WaitForURLAsync("**/vacancies?view=open", new() { WaitUntil = WaitUntilState.Commit, Timeout = 30_000 });
        await _page.WaitForSelectorAsync(".e-grid .e-row, .e-grid .e-emptyrow", new() { Timeout = 30_000 });

        var rows = _page.Locator(".e-grid .e-row");
        if (openCount > 0)
            await Assertions.Expect(rows.First).ToBeVisibleAsync(new() { Timeout = 15_000 });

        var count = await rows.CountAsync();
        for (var i = 0; i < count; i++)
            Assert.Contains("Open", (await rows.Nth(i).InnerTextAsync()).Trim());
    }

    [Fact]
    public async Task StaleVacanciesTile_OpensVacancyList_WithStaleBanner()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new RecruitmentDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);
        await dashboard.GoToAsync();
        var staleCount = await dashboard.GetSummaryTileValueAsync("Stale vacancies");

        await _page.Locator(".widget-kpi-row[role='list'] .widget-kpi")
            .Filter(new() { HasText = "Stale vacancies" }).First.ClickAsync();
        await _page.WaitForURLAsync("**/vacancies?view=stale", new() { WaitUntil = WaitUntilState.Commit, Timeout = 30_000 });
        await _page.WaitForSelectorAsync(".e-grid .e-row, .e-grid .e-emptyrow", new() { Timeout = 30_000 });

        await Assertions.Expect(_page.Locator("[data-testid='vacancy-list-stale-banner']"))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
        Assert.Equal(staleCount, await _page.Locator(".e-grid .e-row").CountAsync());
    }

    [Fact]
    public async Task ListViewToggle_ShowsVacancyList_WithoutPageHeader()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new RecruitmentDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);
        await dashboard.GoToAsync();
        await dashboard.SwitchToListViewAsync();

        await Assertions.Expect(_page.Locator(".dashboard-scroll-x .e-grid")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(_page.Locator(".dashboard-scroll-x h1")).ToHaveCountAsync(0);
        await Assertions.Expect(_page.Locator(".dashboard-scroll-x", new() { HasText = "Track open roles" })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task NewApplicationsTile_DrillDown_RowCountEqualsTileCount()
        => await AssertTileDrillDownAgreesAsync("New applications");

    [Fact]
    public async Task CandidatesInProgressTile_DrillDown_RowCountEqualsTileCount()
        => await AssertTileDrillDownAgreesAsync("Candidates in progress");

    [Fact]
    public async Task InterviewsRequiringActionTile_DrillDown_RowCountEqualsTileCount()
        => await AssertTileDrillDownAgreesAsync("Interviews requiring action");

    [Fact]
    public async Task OffersAwaitingResponseTile_DrillDown_RowCountEqualsTileCount()
    {
        await AssertTileDrillDownAgreesAsync("Offers awaiting response");
    }

    [Fact]
    public async Task ZeroCountTile_OpensDrillDown_WithEmptyState_NotAnError()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new RecruitmentDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);
        await dashboard.GoToAsync();

        foreach (var tile in DrillableTiles)
        {
            var value = await dashboard.GetSummaryTileValueAsync(tile);

            await dashboard.OpenMetricDrillDownAsync(tile);
            Assert.True(await dashboard.IsMetricDrillDownOpenAsync(),
                $"Expected the '{tile}' drill-down dialog to open (tile count {value})");
            Assert.Equal(value, await dashboard.GetDrillDownRowCountAsync());
            await dashboard.CloseMetricDrillDownAsync();
        }
    }

    private async Task AssertTileDrillDownAgreesAsync(string tile)
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new RecruitmentDashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(MarcusEmail);
        await dashboard.GoToAsync();

        var tileValue = await dashboard.GetSummaryTileValueAsync(tile);
        Assert.True(tileValue >= 0);

        await dashboard.OpenMetricDrillDownAsync(tile);
        var rowCount = await dashboard.GetDrillDownRowCountAsync();

        Assert.Equal(tileValue, rowCount);

        await dashboard.CloseMetricDrillDownAsync();
    }
}
