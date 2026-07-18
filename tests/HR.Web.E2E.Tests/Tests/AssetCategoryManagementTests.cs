using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies HR Administrator active/inactive filtering workflow for asset categories:
/// - Deactivate an asset category.
/// - It disappears from the default (active-only) list view.
/// - Toggling "Show Inactive" reveals it again.
/// </summary>
[Collection("E2E")]
public sealed class AssetCategoryManagementTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task DeactivateAssetCategory_HidesFromActiveList_ShowsWhenInactiveToggled()
    {
        var catName = $"E2E Deact {Guid.NewGuid().ToString("N")[..8]}";

        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var catList = new AssetCategoryListPage(_page, _fixture.WebBaseUrl);
        var catEdit = new AssetCategoryEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        // Create first.
        await catList.GoToAsync(AcmeId);
        await catList.ClickNewAsync();
        await catEdit.FillNameAsync(catName);
        await catEdit.SaveAsync();

        // Deactivate.
        await catList.GoToAsync(AcmeId);
        Assert.True(await catList.IsActiveAsync(catName), "Expected newly created asset category to be Active");
        await catList.DeactivateAsync(catName);

        Assert.False(await catList.HasItemAsync(catName),
            $"Expected '{catName}' to no longer appear in the default active-only view after deactivation");

        // Show inactive and verify it reappears.
        await catList.ShowInactiveAsync();

        Assert.True(await catList.HasItemAsync(catName),
            "Expected deactivated asset category to appear when 'Show Inactive' is enabled");
    }
}
