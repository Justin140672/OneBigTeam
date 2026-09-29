using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

public sealed class AssetCategoryManagementTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
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

        await catList.GoToAsync(AcmeId);
        await catList.ClickNewAsync();
        await catEdit.FillNameAsync(catName);
        await catEdit.SaveAsync();

        await catList.GoToAsync(AcmeId);
        Assert.True(await catList.IsActiveAsync(catName), "Expected newly created asset category to be Active");
        await catList.DeactivateAsync(catName);

        Assert.False(await catList.HasItemAsync(catName),
            $"Expected '{catName}' to no longer appear in the default active-only view after deactivation");

        await catList.ShowInactiveAsync();

        Assert.True(await catList.HasItemAsync(catName),
            "Expected deactivated asset category to appear when 'Show Inactive' is enabled");
    }
}
