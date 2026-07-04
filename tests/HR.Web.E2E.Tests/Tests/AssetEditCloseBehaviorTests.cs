using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Direct coverage of the Close / unsaved-changes prompt (EditPageBase) on the Asset edit page.
/// </summary>
[Collection("E2E")]
public sealed class AssetEditCloseBehaviorTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";
    // Seeded asset category for Acme (see AssetsModule.cs seed data).
    private const string SeededCategory = "IT Equipment";

    private static string RandomAssetNumber() => $"E2E-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";

    [Fact]
    public async Task Close_ExistingRecordWithNoChanges_NavigatesDirectlyToList()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var assetList = new AssetListPage(_page, _fixture.WebBaseUrl);
        var assetEdit = new AssetEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var assetNumber = RandomAssetNumber();
        var assetName = $"E2E Close {Guid.NewGuid().ToString("N")[..8]}";
        await assetList.GoToAsync(AcmeId);
        await assetList.ClickNewAsync();
        await assetEdit.FillAssetNumberAsync(assetNumber);
        await assetEdit.FillNameAsync(assetName);
        await assetEdit.SelectCategoryAsync(SeededCategory);
        await assetEdit.SaveAsync();

        await assetList.GoToAsync(AcmeId);
        var href = await _page.Locator(".e-rowcell a").Filter(new() { HasText = assetNumber }).First.GetAttributeAsync("href");
        Assert.NotNull(href);
        await _page.GotoAsync($"{_fixture.WebBaseUrl}{href}");
        await _page.WaitForSelectorAsync("span[role='combobox']", new() { Timeout = 20_000 });

        await assetEdit.CloseAndWaitForListAsync();

        Assert.EndsWith("/assets", _page.Url);
    }

    [Fact]
    public async Task Close_NewRecordWithUnsavedChanges_ShowsConfirmDialog()
    {
        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var assetEdit = new AssetEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await assetEdit.GoToNewAsync(AcmeId);
        await assetEdit.FillNameAsync("Unsaved Asset");

        await assetEdit.ClickCloseAsync();

        Assert.True(await assetEdit.IsUnsavedChangesDialogVisibleAsync());
        Assert.Contains("/assets/new", _page.Url);
    }

    [Fact]
    public async Task Close_DiscardChanges_NavigatesAwayWithoutSaving()
    {
        var assetName = $"E2E Discard {Guid.NewGuid().ToString("N")[..8]}";

        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var assetList = new AssetListPage(_page, _fixture.WebBaseUrl);
        var assetEdit = new AssetEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await assetEdit.GoToNewAsync(AcmeId);
        await assetEdit.FillAssetNumberAsync(RandomAssetNumber());
        await assetEdit.FillNameAsync(assetName);
        await assetEdit.SelectCategoryAsync(SeededCategory);

        await assetEdit.ClickCloseAsync();
        Assert.True(await assetEdit.IsUnsavedChangesDialogVisibleAsync());

        await assetEdit.ConfirmDiscardChangesAsync();

        Assert.EndsWith("/assets", _page.Url);
        await assetList.GoToAsync(AcmeId);
        Assert.False(await assetList.HasItemAsync(assetName));
    }

    [Fact]
    public async Task Close_SaveFromUnsavedChangesDialog_SavesAndNavigatesToList()
    {
        var assetName = $"E2E SaveOnClose {Guid.NewGuid().ToString("N")[..8]}";

        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var assetList = new AssetListPage(_page, _fixture.WebBaseUrl);
        var assetEdit = new AssetEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await assetEdit.GoToNewAsync(AcmeId);
        await assetEdit.FillAssetNumberAsync(RandomAssetNumber());
        await assetEdit.FillNameAsync(assetName);
        await assetEdit.SelectCategoryAsync(SeededCategory);

        await assetEdit.ClickCloseAsync();
        Assert.True(await assetEdit.IsUnsavedChangesDialogVisibleAsync());

        await assetEdit.ConfirmSaveFromUnsavedChangesDialogAsync();

        Assert.EndsWith("/assets", _page.Url);
        await assetList.GoToAsync(AcmeId);
        Assert.True(await assetList.HasItemAsync(assetName));
    }

    [Fact]
    public async Task Close_CancelUnsavedChangesDialog_StaysOnPageWithFieldIntact()
    {
        var assetName = $"E2E CancelClose {Guid.NewGuid().ToString("N")[..8]}";

        var login     = new LoginPage(_page, _fixture.WebBaseUrl);
        var assetEdit = new AssetEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await assetEdit.GoToNewAsync(AcmeId);
        await assetEdit.FillNameAsync(assetName);

        await assetEdit.ClickCloseAsync();
        Assert.True(await assetEdit.IsUnsavedChangesDialogVisibleAsync());

        await assetEdit.CancelUnsavedChangesDialogAsync();

        Assert.Contains("/assets/new", _page.Url);
        Assert.Equal(assetName, await assetEdit.GetNameAsync());
    }
}
