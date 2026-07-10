using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the Assets tab on the HR-admin employee edit page.
///
/// Uses seeded data:
///   - Tom Williams  (30000000-0000-0000-0000-000000000004) has a MacBook Pro 14"
///     assigned (ASSET-0001, assignment c0000000-0000-0000-0000-000000000003).
///   - Sarah Chen    (30000000-0000-0000-0000-000000000001) has a Dell UltraSharp 27"
///     assigned (ASSET-0002, assignment c0000000-0000-0000-0000-000000000005).
///   - Carlos Rivera (30000000-0000-0000-0000-000000000010) has no assigned assets.
///
/// Admin user: Laura Bennett (laura.bennett@acme.example) who holds the
/// employee:manage permission.
/// </summary>
[Collection("E2E")]
public sealed class EmployeeAssetsTabTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId   = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId    = Guid.Parse("30000000-0000-0000-0000-000000000004");
    private static readonly Guid SarahId  = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid CarlosId = Guid.Parse("30000000-0000-0000-0000-000000000010");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task AssetsTab_IsVisible_OnAdminEmployeeEditPage()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empAdmin = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empAdmin.GoToAsync(AcmeId, TomId);

        var assetsTab = _page.GetByRole(Microsoft.Playwright.AriaRole.Tab, new() { Name = "Assets" });
        Assert.True(await assetsTab.IsVisibleAsync(),
            "The Assets tab should be present on the admin employee edit page");
    }

    [Fact]
    public async Task AssetsTab_ShowsGrid_WithAssignedAssets_ForTom()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empAdmin = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empAdmin.GoToAsync(AcmeId, TomId);
        await empAdmin.OpenAssetsTabAsync();

        Assert.True(await empAdmin.HasAssetsGridRowsAsync(),
            "Expected the admin assets grid to contain at least one row for Tom who has a seeded asset");
    }

    [Fact]
    public async Task AssetsTab_ShowsCorrectAssetNumber_ForTom()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empAdmin = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empAdmin.GoToAsync(AcmeId, TomId);
        await empAdmin.OpenAssetsTabAsync();

        var assetNumbers = await empAdmin.GetAssetsGridAssetNumbersAsync();
        Assert.Contains("ASSET-0001", assetNumbers);
    }

    [Fact]
    public async Task AssetsTab_ShowsGrid_WithAssignedAssets_ForSarah()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empAdmin = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empAdmin.GoToAsync(AcmeId, SarahId);
        await empAdmin.OpenAssetsTabAsync();

        Assert.True(await empAdmin.HasAssetsGridRowsAsync(),
            "Expected the admin assets grid to contain at least one row for Sarah who has a seeded asset");

        var assetNumbers = await empAdmin.GetAssetsGridAssetNumbersAsync();
        Assert.Contains("ASSET-0002", assetNumbers);
    }

    [Fact]
    public async Task AssetsTab_ShowsEmptyGrid_ForEmployeeWithoutAssets()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empAdmin = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empAdmin.GoToAsync(AcmeId, CarlosId);
        await empAdmin.OpenAssetsTabAsync();

        Assert.False(await empAdmin.HasAssetsGridRowsAsync(),
            "Expected no rows in the assets grid for Carlos who has no seeded assets");
    }

    [Fact]
    public async Task AssetsTab_ShowsAssignAssetButton()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empAdmin = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empAdmin.GoToAsync(AcmeId, TomId);
        await empAdmin.OpenAssetsTabAsync();

        Assert.True(await empAdmin.HasAssignAssetButtonAsync(),
            "Expected the 'Assign Asset' button to be visible on the admin Assets tab");
    }

    [Fact]
    public async Task AssetsTab_ShowsReturnAssetButton()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empAdmin = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empAdmin.GoToAsync(AcmeId, TomId);
        await empAdmin.OpenAssetsTabAsync();

        Assert.True(await empAdmin.HasReturnAssetButtonAsync(),
            "Expected the 'Return Asset' button to be visible on the admin Assets tab");
    }

    [Fact]
    public async Task AssetsTab_ReturnAssetButton_IsDisabled_WhenNoAssetsAssigned()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empAdmin = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // Carlos has no assigned assets, so there is nothing to return.
        await empAdmin.GoToAsync(AcmeId, CarlosId);
        await empAdmin.OpenAssetsTabAsync();

        Assert.True(await empAdmin.IsReturnAssetButtonDisabledAsync(),
            "Expected the 'Return Asset' button to be disabled when the employee has no assigned assets");
    }

    [Fact]
    public async Task AssetsTab_ClickingAssignAsset_OpensDialog()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empAdmin = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empAdmin.GoToAsync(AcmeId, TomId);
        await empAdmin.OpenAssetsTabAsync();

        await empAdmin.OpenAssignAssetDialogAsync();

        Assert.True(await empAdmin.IsAssignAssetDialogVisibleAsync(),
            "Expected the Assign Asset dialog to open after clicking the button");
    }

    [Fact]
    public async Task AssetsTab_AssignAssetDialog_CanBeDismissed()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empAdmin = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empAdmin.GoToAsync(AcmeId, TomId);
        await empAdmin.OpenAssetsTabAsync();

        await empAdmin.OpenAssignAssetDialogAsync();
        Assert.True(await empAdmin.IsAssignAssetDialogVisibleAsync(),
            "Dialog should be open before dismissal");

        await empAdmin.CloseAssignAssetDialogAsync();
        Assert.False(await empAdmin.IsAssignAssetDialogVisibleAsync(),
            "Expected the Assign Asset dialog to close after clicking Cancel");
    }

    [Fact]
    public async Task AssetsTab_AssigningAvailableAsset_AddsRowToGrid()
    {
        // Carlos has no assets. ASSET-0003 (Logitech MX Keys) is seeded as Available.
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var empAdmin = new EmployeeAdminPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empAdmin.GoToAsync(AcmeId, CarlosId);
        await empAdmin.OpenAssetsTabAsync();

        Assert.False(await empAdmin.HasAssetsGridRowsAsync(),
            "Carlos should have no assets before assignment");

        await empAdmin.OpenAssignAssetDialogAsync();
        await empAdmin.SelectAssetAndConfirmAsync("ASSET-0003");

        Assert.True(await empAdmin.HasAssetsGridRowsAsync(),
            "Carlos should have one asset row after assignment");

        var assetNumbers = await empAdmin.GetAssetsGridAssetNumbersAsync();
        Assert.Contains("ASSET-0003", assetNumbers);
    }
}
