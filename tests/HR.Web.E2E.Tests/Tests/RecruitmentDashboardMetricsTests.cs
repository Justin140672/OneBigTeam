using HR.Web.E2E.Tests.Infrastructure;
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
