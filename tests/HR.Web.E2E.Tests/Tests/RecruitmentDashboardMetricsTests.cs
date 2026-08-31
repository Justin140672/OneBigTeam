using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// DSH-04: the Recruiter-only dashboard's summary row
/// (src/HR.Web/Components/Pages/Dashboards/RecruitmentDashboard.razor) now feeds each metric tile
/// from an authoritative server metric endpoint that returns { count, items[] } with
/// count == items.Count, and every drillable tile opens RecruitmentMetricDrillDownDialog.razor
/// listing exactly those items. So the drill-down row count must always equal the number shown on
/// the tile.
///
/// Drillable tiles: "New applications", "Candidates in progress" (new tile), "Interviews requiring
/// action", "Offers awaiting response". "Open vacancies" and "Stale vacancies" navigate to the
/// vacancies list instead of opening a drill-down and are covered by RecruitmentDashboardTests /
/// RecruitmentDashboardRedesignTests.
///
/// Uses the seeded Acme company and Marcus Diallo (the only seeded Recruiter persona), consistent
/// with the other RecruitmentDashboard* test classes. Counts are not asserted to exact values
/// (other tests mutate the shared Acme recruitment data) — only tile/drill-down agreement and
/// non-negativity.
/// </summary>
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
        // If the seeded pipeline has no Offer-purpose stage the tile renders muted at 0 and its
        // drill-down is empty — that is still valid tile/drill-down agreement (0 == 0).
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

        // Open every drillable tile in turn. Any tile currently at 0 must still open a dialog that
        // shows the empty state (0 rows) rather than erroring; a non-zero tile must still agree with
        // its drill-down. Covers the zero branch whichever tile happens to be empty on this run
        // against the shared Acme data.
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
