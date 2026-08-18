using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Direct coverage of the Close / unsaved-changes prompt (EditPageBase) on the Asset Category
/// edit page.
/// </summary>
public sealed class AssetCategoryEditCloseBehaviorTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task Close_ExistingRecordWithNoChanges_NavigatesDirectlyToList()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var catList  = new AssetCategoryListPage(_page, _fixture.WebBaseUrl);
        var catEdit  = new AssetCategoryEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var catName = $"E2E Close {Guid.NewGuid().ToString("N")[..8]}";
        await catList.GoToAsync(AcmeId);
        await catList.ClickNewAsync();
        await catEdit.FillNameAsync(catName);
        await catEdit.SaveAsync();

        await catList.GoToAsync(AcmeId);
        var href = await _page.Locator(".e-rowcell a").Filter(new() { HasText = catName }).First.GetAttributeAsync("href");
        Assert.NotNull(href);
        await _page.GotoAsync($"{_fixture.WebBaseUrl}{href}");
        await _page.WaitForSelectorAsync("button:has-text('Save')", new() { Timeout = 20_000 });

        await catEdit.CloseAndWaitForListAsync();

        Assert.EndsWith("/asset-categories", _page.Url);
    }

    [Fact]
    public async Task Close_NewRecordWithUnsavedChanges_ShowsConfirmDialog()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var catEdit = new AssetCategoryEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await catEdit.GoToNewAsync(AcmeId);
        await catEdit.FillNameAsync("Unsaved Asset Category");

        await catEdit.ClickCloseAsync();

        Assert.True(await catEdit.IsUnsavedChangesDialogVisibleAsync());
        Assert.Contains("/asset-categories/new", _page.Url);
    }

    [Fact]
    public async Task Close_DiscardChanges_NavigatesAwayWithoutSaving()
    {
        var catName = $"E2E Discard {Guid.NewGuid().ToString("N")[..8]}";

        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var catList = new AssetCategoryListPage(_page, _fixture.WebBaseUrl);
        var catEdit = new AssetCategoryEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await catEdit.GoToNewAsync(AcmeId);
        await catEdit.FillNameAsync(catName);

        await catEdit.ClickCloseAsync();
        Assert.True(await catEdit.IsUnsavedChangesDialogVisibleAsync());

        await catEdit.ConfirmDiscardChangesAsync();

        Assert.EndsWith("/asset-categories", _page.Url);
        await catList.GoToAsync(AcmeId);
        Assert.False(await catList.HasItemAsync(catName));
    }

    [Fact]
    public async Task Close_SaveFromUnsavedChangesDialog_SavesAndNavigatesToList()
    {
        var catName = $"E2E SaveOnClose {Guid.NewGuid().ToString("N")[..8]}";

        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var catList = new AssetCategoryListPage(_page, _fixture.WebBaseUrl);
        var catEdit = new AssetCategoryEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await catEdit.GoToNewAsync(AcmeId);
        await catEdit.FillNameAsync(catName);

        await catEdit.ClickCloseAsync();
        Assert.True(await catEdit.IsUnsavedChangesDialogVisibleAsync());

        await catEdit.ConfirmSaveFromUnsavedChangesDialogAsync();

        Assert.EndsWith("/asset-categories", _page.Url);
        await catList.GoToAsync(AcmeId);
        Assert.True(await catList.HasItemAsync(catName));
    }

    [Fact]
    public async Task Close_CancelUnsavedChangesDialog_StaysOnPageWithFieldIntact()
    {
        var catName = $"E2E CancelClose {Guid.NewGuid().ToString("N")[..8]}";

        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var catEdit = new AssetCategoryEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await catEdit.GoToNewAsync(AcmeId);
        await catEdit.FillNameAsync(catName);

        await catEdit.ClickCloseAsync();
        Assert.True(await catEdit.IsUnsavedChangesDialogVisibleAsync());

        await catEdit.CancelUnsavedChangesDialogAsync();

        Assert.Contains("/asset-categories/new", _page.Url);
        Assert.Equal(catName, await catEdit.GetNameAsync());
    }
}
