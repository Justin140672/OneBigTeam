using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the My Assets dashboard widget.
///
/// Uses seeded data from AssetsModule.SeedAssetsAsync:
///   - ASSET-0001 (MacBook Pro 14") assigned to Tom Williams (tom.williams@acme.example)
///     employee ID 30000000-…-0004 — assignment is unacknowledged → shows "Pending" badge.
///   - ASSET-0002 (Dell UltraSharp 27") assigned to Sarah Chen (sarah.chen@acme.example)
///     employee ID 30000000-…-0001 — assignment is unacknowledged → shows "Pending" badge.
///   - Laura Bennett (laura.bennett@acme.example) has an employee record but no asset
///     assignments → widget shows empty state.
/// </summary>
[Collection("E2E")]
public sealed class MyAssetsWidgetTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId     = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomAssetId = Guid.Parse("c0000000-0000-0000-0000-000000000002");

    private const string TomEmail   = "tom.williams@acme.example";
    private const string SarahEmail = "sarah.chen@acme.example";
    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task Dashboard_ShowsMyAssetsWidget()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new DashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);
        await dashboard.GoToAsync();

        Assert.True(await dashboard.HasMyAssetsWidgetAsync(),
            "Expected the 'My Assets' widget header to be visible on the dashboard");
    }

    [Fact]
    public async Task Dashboard_MyAssetsWidget_ShowsAssignedAssetName()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new DashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);
        await dashboard.GoToAsync();

        var names = await dashboard.GetMyAssetNamesAsync();

        Assert.True(
            names.Any(n => n.Contains("MacBook", StringComparison.OrdinalIgnoreCase)),
            $"Expected 'MacBook Pro 14\"' to appear in the My Assets widget. " +
            $"Names found: [{string.Join(", ", names)}]");
    }

    [Fact]
    public async Task Dashboard_MyAssetsWidget_ShowsEmptyState_WhenNoAssetsAssigned()
    {
        // Laura Bennett is an HR Manager with an employee record but no asset assignments.
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new DashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await dashboard.GoToAsync();

        Assert.True(await dashboard.IsMyAssetsWidgetEmptyAsync(),
            "Expected the My Assets widget to show the 'No assets assigned.' empty state for a user with no assets");
    }

    [Fact]
    public async Task Dashboard_MyAssetsWidget_ShowsPendingBadge_ForUnacknowledgedAsset()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new DashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);
        await dashboard.GoToAsync();

        var badge = await dashboard.GetMyAssetAcknowledgementBadgeAsync("MacBook");

        Assert.Equal("Pending", badge);
    }

    [Fact]
    public async Task Dashboard_MyAssetsWidget_ShowsPendingBadge_ForSarahsUnacknowledgedAsset()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new DashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(SarahEmail);
        await dashboard.GoToAsync();

        var badge = await dashboard.GetMyAssetAcknowledgementBadgeAsync("Dell");

        Assert.Equal("Pending", badge);
    }

    [Fact]
    public async Task Dashboard_MyAssetsWidget_ClickingAsset_NavigatesToAssetDetailPage()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new DashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);
        await dashboard.GoToAsync();

        await dashboard.ClickMyAssetAsync("MacBook");

        Assert.Contains($"/assets/{TomAssetId}/view", _page.Url,
            StringComparison.OrdinalIgnoreCase);
    }
}
