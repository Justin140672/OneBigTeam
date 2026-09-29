using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

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
    public async Task AssetDetail_IsDenied_WhenEmployeeNavigatesToAnotherEmployeesAsset()
    {
        // Resource-level authorization (self / direct manager / HR administrator): Tom Williams is a
        // plain employee and must not see the asset assigned to Sarah Chen, so the API refuses the
        // read and the page shows the "Asset not found" alert instead of the detail content.
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var detail = new AssetDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/assets/{SarahAssetId}/view");
        await _page.WaitForSelectorAsync(".alert-danger", new() { Timeout = 20_000 });

        Assert.True(await detail.IsNotFoundAlertVisibleAsync());
        Assert.False(await _page.Locator("[data-testid='asset-detail-content']").IsVisibleAsync());
    }
}
