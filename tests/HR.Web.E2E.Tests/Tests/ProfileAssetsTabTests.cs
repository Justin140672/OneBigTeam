using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the Assets tab on the self-service My Profile page.
///
/// Uses seeded data:
///   - Sarah Chen (30000000-0000-0000-0000-000000000001) has a Dell UltraSharp 27"
///     monitor assigned (ASSET-0002, assignment c0000000-0000-0000-0000-000000000005).
///   - Tom Williams (30000000-0000-0000-0000-000000000004) has a MacBook Pro 14"
///     assigned (ASSET-0001, assignment c0000000-0000-0000-0000-000000000003).
/// </summary>
[Collection("E2E")]
public sealed class ProfileAssetsTabTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId   = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid SarahId  = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId    = Guid.Parse("30000000-0000-0000-0000-000000000004");
    private static readonly Guid CarlosId = Guid.Parse("30000000-0000-0000-0000-000000000010");

    private const string SarahEmail  = "sarah.chen@acme.example";
    private const string TomEmail    = "tom.williams@acme.example";
    private const string CarlosEmail = "carlos.rivera@acme.example";

    [Fact]
    public async Task AssetsTab_IsVisible_OnMyProfile()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(SarahEmail);

        await profile.GoToAsync(AcmeId, SarahId);

        var assetsTab = _page.GetByRole(AriaRole.Tab, new() { Name = "Assets" });
        Assert.True(await assetsTab.IsVisibleAsync(), "Assets tab should be visible on My Profile");
    }

    [Fact]
    public async Task AssetsTab_ShowsAssignedAssets_ForSarah()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(SarahEmail);

        await profile.GoToAsync(AcmeId, SarahId);
        await profile.OpenAssetsTabAsync();

        Assert.True(await profile.HasAssetsTableAsync(),
            "Expected the assets table to be visible for Sarah who has a seeded asset");

        var assetNumbers = await profile.GetAssetNumbersAsync();
        Assert.Contains("ASSET-0002", assetNumbers);
    }

    [Fact]
    public async Task AssetsTab_ShowsCorrectAssetCount_ForSarah()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(SarahEmail);

        await profile.GoToAsync(AcmeId, SarahId);
        await profile.OpenAssetsTabAsync();

        var count = await profile.GetAssetRowCountAsync();
        Assert.True(count >= 1, $"Expected at least one asset row for Sarah, got {count}");
    }

    [Fact]
    public async Task AssetsTab_ShowsAssignedAssets_ForTom()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await profile.GoToAsync(AcmeId, TomId);
        await profile.OpenAssetsTabAsync();

        Assert.True(await profile.HasAssetsTableAsync(),
            "Expected the assets table to be visible for Tom who has a seeded asset");

        var assetNumbers = await profile.GetAssetNumbersAsync();
        Assert.Contains("ASSET-0001", assetNumbers);
    }

    [Fact]
    public async Task AssetsTab_ClickingViewAsset_OpensDialog_WithoutNavigatingAway()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(SarahEmail);

        await profile.GoToAsync(AcmeId, SarahId);
        await profile.OpenAssetsTabAsync();

        var profileUrlBeforeClick = _page.Url;

        // Select the row, then click the "View" toolbar action once it's enabled.
        await _page.Locator(".e-grid .e-row").First.ClickAsync();
        await _page.WaitForFunctionAsync(
            "!document.querySelector('[id=\"hr-view\"]')?.classList?.contains('e-overlay')",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        await _page.Locator("[id='hr-view']").ClickAsync();

        // Should open the asset in a dialog (AssetDetailDialog), not navigate to /assets/{id}/view.
        await _page.WaitForSelectorAsync(".asset-detail-dialog", new() { Timeout = 15_000 });
        Assert.True(await _page.Locator(".asset-detail-dialog").IsVisibleAsync(),
            "Expected clicking View on My Profile's Assets tab to open the asset in a dialog");
        Assert.Equal(profileUrlBeforeClick, _page.Url);
    }

    [Fact]
    public async Task AssetsTab_IsVisible_And_Shows_Empty_State_For_Employee_Without_Assets()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(CarlosEmail);

        await profile.GoToAsync(AcmeId, CarlosId);

        var assetsTab = _page.GetByRole(AriaRole.Tab, new() { Name = "Assets" });
        Assert.True(await assetsTab.IsVisibleAsync(),
            "Assets tab should be visible even when the employee has no assigned assets");

        await profile.OpenAssetsTabAsync();

        Assert.False(await profile.HasAssetsTableAsync(),
            "Expected no asset rows for Carlos who has no seeded assets");
    }
}
