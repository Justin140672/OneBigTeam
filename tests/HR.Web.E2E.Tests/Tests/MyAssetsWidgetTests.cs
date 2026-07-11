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
///     Sarah is checked directly on her profile's Assets tab elsewhere; the dashboard-widget
///     "Pending" check below uses Laura instead (see note).
///   - ASSET-0007 (Dell Latitude 5440) assigned to Laura Bennett (laura.bennett@acme.example)
///     employee ID 30000000-…-0005 — assignment is unacknowledged → shows "Pending" badge.
///   - Carlos Rivera (carlos.rivera@acme.example) has an employee record but no asset
///     assignments → widget shows empty state.
///
/// Note: Sarah Chen is seeded as CompanyAdministrator + Manager (Manager grants no
/// EmployeeEdit — it's needed only so she satisfies the "probation:review" policy for
/// reviews she's assigned as manager on), and per Home.razor's redirect logic
/// (CanManageCompany &amp;&amp; !CanManageEmployees), she is sent straight to her company edit page
/// instead of "/" and can never reach the dashboard. The "Pending" badge scenario previously
/// covered by Sarah below now uses Laura, who was given a parallel unacknowledged asset
/// assignment for this purpose. The empty-state scenario previously used Laura, but she now
/// has an asset assignment, so it was moved to Carlos Rivera, who has zero asset assignments.
/// </summary>
[Collection("E2E")]
public sealed class MyAssetsWidgetTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string TomEmail    = "tom.williams@acme.example";
    private const string LauraEmail  = "laura.bennett@acme.example";
    private const string CarlosEmail = "carlos.rivera@acme.example";

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
        // Carlos Rivera is an Account Executive with an employee record but no asset
        // assignments. Laura Bennett previously covered this case, but she was given a
        // seeded asset assignment (see class doc comment) so a different zero-asset employee
        // is used here instead — verified against AssetsModule.SeedAssetsAsync, where only
        // Tom Williams and Laura Bennett have assigned assets.
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new DashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(CarlosEmail);
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
    public async Task Dashboard_MyAssetsWidget_ShowsPendingBadge_ForLaurasUnacknowledgedAsset()
    {
        // Sarah Chen is seeded as CompanyAdministrator-only and is redirected away from "/"
        // (see Home.razor), so she can no longer reach the dashboard to check this widget.
        // Laura Bennett was given a parallel unacknowledged Dell asset assignment for this test.
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard = new DashboardPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);
        await dashboard.GoToAsync();

        var badge = await dashboard.GetMyAssetAcknowledgementBadgeAsync("Dell");

        Assert.Equal("Pending", badge);
    }

    [Fact]
    public async Task Dashboard_MyAssetsWidget_ClickingAsset_OpensAssetDetailDialog()
    {
        var login       = new LoginPage(_page, _fixture.WebBaseUrl);
        var dashboard   = new DashboardPage(_page, _fixture.WebBaseUrl);
        var assetDetail = new AssetDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);
        await dashboard.GoToAsync();

        // Clicking an asset item opens AssetDetailDialog in place (no navigation).
        await dashboard.ClickMyAssetAsync("MacBook");

        Assert.Contains("MacBook", await assetDetail.GetAssetNameAsync(),
            StringComparison.OrdinalIgnoreCase);
    }
}
