using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Direct coverage of the Close / unsaved-changes prompt (EditPageBase) on the Position
/// Profile edit page.
/// </summary>
[Collection("E2E")]
public sealed class PositionProfileEditCloseBehaviorTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task Close_ExistingRecordWithNoChanges_NavigatesDirectlyToList()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var ppList   = new PositionProfileListPage(_page, _fixture.WebBaseUrl);
        var ppEdit   = new PositionProfileEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var title = $"E2E Close {Guid.NewGuid().ToString("N")[..8]}";
        await ppList.GoToAsync(AcmeId);
        await ppList.ClickNewPositionProfileAsync();
        await ppEdit.FillTitleAsync(title);
        // Department, Location and Default Leave Policy are now mandatory on Position Profile.
        await ppEdit.SelectDepartmentAsync("Engineering");
        await ppEdit.SelectLocationAsync("London Office");
        await ppEdit.SelectDefaultLeavePolicyAsync("Standard");
        await ppEdit.SaveAsync();

        await ppList.GoToAsync(AcmeId);
        var href = await _page.Locator(".e-rowcell a").Filter(new() { HasText = title }).First.GetAttributeAsync("href");
        Assert.NotNull(href);
        await _page.GotoAsync($"{_fixture.WebBaseUrl}{href}");
        await _page.WaitForSelectorAsync("span[role='combobox']", new() { Timeout = 20_000 });

        await ppEdit.CloseAndWaitForListAsync();

        Assert.EndsWith("/position-profiles", _page.Url);
    }

    [Fact]
    public async Task Close_NewRecordWithUnsavedChanges_ShowsConfirmDialog()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var ppEdit = new PositionProfileEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await ppEdit.GoToNewAsync(AcmeId);
        await ppEdit.FillTitleAsync("Unsaved Position Profile");

        await ppEdit.ClickCloseAsync();

        Assert.True(await ppEdit.IsUnsavedChangesDialogVisibleAsync());
        Assert.Contains("/position-profiles/new", _page.Url);
    }

    [Fact]
    public async Task Close_DiscardChanges_NavigatesAwayWithoutSaving()
    {
        var title = $"E2E Discard {Guid.NewGuid().ToString("N")[..8]}";

        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var ppList = new PositionProfileListPage(_page, _fixture.WebBaseUrl);
        var ppEdit = new PositionProfileEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await ppEdit.GoToNewAsync(AcmeId);
        await ppEdit.FillTitleAsync(title);

        await ppEdit.ClickCloseAsync();
        Assert.True(await ppEdit.IsUnsavedChangesDialogVisibleAsync());

        await ppEdit.ConfirmDiscardChangesAsync();

        Assert.EndsWith("/position-profiles", _page.Url);
        await ppList.GoToAsync(AcmeId);
        Assert.False(await ppList.HasPositionProfileAsync(title));
    }

    [Fact]
    public async Task Close_SaveFromUnsavedChangesDialog_SavesAndNavigatesToList()
    {
        var title = $"E2E SaveOnClose {Guid.NewGuid().ToString("N")[..8]}";

        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var ppList = new PositionProfileListPage(_page, _fixture.WebBaseUrl);
        var ppEdit = new PositionProfileEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await ppEdit.GoToNewAsync(AcmeId);
        await ppEdit.FillTitleAsync(title);

        await ppEdit.ClickCloseAsync();
        Assert.True(await ppEdit.IsUnsavedChangesDialogVisibleAsync());

        await ppEdit.ConfirmSaveFromUnsavedChangesDialogAsync();

        Assert.EndsWith("/position-profiles", _page.Url);
        await ppList.GoToAsync(AcmeId);
        Assert.True(await ppList.HasPositionProfileAsync(title));
    }

    [Fact]
    public async Task Close_CancelUnsavedChangesDialog_StaysOnPageWithFieldIntact()
    {
        var title = $"E2E CancelClose {Guid.NewGuid().ToString("N")[..8]}";

        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var ppEdit = new PositionProfileEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await ppEdit.GoToNewAsync(AcmeId);
        await ppEdit.FillTitleAsync(title);

        await ppEdit.ClickCloseAsync();
        Assert.True(await ppEdit.IsUnsavedChangesDialogVisibleAsync());

        await ppEdit.CancelUnsavedChangesDialogAsync();

        Assert.Contains("/position-profiles/new", _page.Url);
        Assert.Equal(title, await ppEdit.GetTitleAsync());
    }
}
