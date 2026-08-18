using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the asset detail page at /companies/{companyId}/assets/{id}/view.
///
/// Uses seeded data from AssetsModule.SeedAssetsAsync:
///   - ASSET-0001 (MacBook Pro 14") — Tom Williams — ID c0000000-0000-0000-0000-000000000002
///   - ASSET-0002 (Dell UltraSharp 27") — Sarah Chen — ID c0000000-0000-0000-0000-000000000004
///   Both belong to company 00000000-0000-0000-0000-000000000001 (Acme Corp).
/// </summary>
public sealed class AssetDetailPageTests(EmployeePersonaFixture fixture) : RoleE2ETestBase<EmployeePersonaFixture>(fixture)
{
    private static readonly Guid AcmeId      = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomAssetId  = Guid.Parse("c0000000-0000-0000-0000-000000000002");
    private static readonly Guid SarahAssetId = Guid.Parse("c0000000-0000-0000-0000-000000000004");

    private const string TomEmail = "tom.williams@acme.example";

    [Fact]
    public async Task AssetDetail_ShowsAssetNumber()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var detail = new AssetDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await detail.GoToAsync(AcmeId, TomAssetId);

        Assert.Equal("ASSET-0001", await detail.GetAssetNumberAsync());
    }

    [Fact]
    public async Task AssetDetail_ShowsAssetName()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var detail = new AssetDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await detail.GoToAsync(AcmeId, TomAssetId);

        Assert.Equal("MacBook Pro 14\"", await detail.GetAssetNameAsync());
    }

    [Fact]
    public async Task AssetDetail_ShowsCategoryName()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var detail = new AssetDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await detail.GoToAsync(AcmeId, TomAssetId);

        Assert.Equal("IT Equipment", await detail.GetCategoryAsync());
    }

    [Fact]
    public async Task AssetDetail_ShowsManufacturerAndModel()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var detail = new AssetDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await detail.GoToAsync(AcmeId, TomAssetId);

        Assert.Equal("Apple", await detail.GetManufacturerAsync());
        Assert.Equal("MacBook Pro 14-inch M3", await detail.GetModelAsync());
    }

    [Fact]
    public async Task AssetDetail_ShowsDifferentAsset_WhenNavigatingToSarahAsset()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var detail = new AssetDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await detail.GoToAsync(AcmeId, SarahAssetId);

        Assert.Equal("ASSET-0002", await detail.GetAssetNumberAsync());
        Assert.Equal("Dell UltraSharp 27\"", await detail.GetAssetNameAsync());
    }
}
