using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Direct coverage of the Close / unsaved-changes prompt (EditPageBase) on the Leave Policy
/// edit page.
/// </summary>
[Collection("E2E")]
public sealed class LeavePolicyEditCloseBehaviorTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task Close_ExistingRecordWithNoChanges_NavigatesDirectlyToList()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var polList  = new LeavePolicyListPage(_page, _fixture.WebBaseUrl);
        var polEdit  = new LeavePolicyEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var polName = $"E2E Close {Guid.NewGuid().ToString("N")[..8]}";
        await polList.GoToAsync(AcmeId);
        await polList.ClickNewAsync();
        await polEdit.FillNameAsync(polName);
        await polEdit.SaveAsync();

        await polList.GoToAsync(AcmeId);
        var href = await _page.Locator(".e-rowcell a").Filter(new() { HasText = polName }).First.GetAttributeAsync("href");
        Assert.NotNull(href);
        await _page.GotoAsync($"{_fixture.WebBaseUrl}{href}");
        await _page.WaitForSelectorAsync("button:has-text('Save')", new() { Timeout = 20_000 });

        await polEdit.CloseAndWaitForListAsync();

        Assert.EndsWith("/leave-policies", _page.Url);
    }

    [Fact]
    public async Task Close_NewRecordWithUnsavedChanges_ShowsConfirmDialog()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var polEdit = new LeavePolicyEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await polEdit.GoToNewAsync(AcmeId);
        await polEdit.FillNameAsync("Unsaved Leave Policy");

        await polEdit.ClickCloseAsync();

        Assert.True(await polEdit.IsUnsavedChangesDialogVisibleAsync());
        Assert.Contains("/leave-policies/new", _page.Url);
    }

    [Fact]
    public async Task Close_DiscardChanges_NavigatesAwayWithoutSaving()
    {
        var polName = $"E2E Discard {Guid.NewGuid().ToString("N")[..8]}";

        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var polList = new LeavePolicyListPage(_page, _fixture.WebBaseUrl);
        var polEdit = new LeavePolicyEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await polEdit.GoToNewAsync(AcmeId);
        await polEdit.FillNameAsync(polName);

        await polEdit.ClickCloseAsync();
        Assert.True(await polEdit.IsUnsavedChangesDialogVisibleAsync());

        await polEdit.ConfirmDiscardChangesAsync();

        Assert.EndsWith("/leave-policies", _page.Url);
        await polList.GoToAsync(AcmeId);
        Assert.False(await polList.HasItemAsync(polName));
    }

    [Fact]
    public async Task Close_SaveFromUnsavedChangesDialog_SavesAndNavigatesToList()
    {
        var polName = $"E2E SaveOnClose {Guid.NewGuid().ToString("N")[..8]}";

        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var polList = new LeavePolicyListPage(_page, _fixture.WebBaseUrl);
        var polEdit = new LeavePolicyEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await polEdit.GoToNewAsync(AcmeId);
        await polEdit.FillNameAsync(polName);

        await polEdit.ClickCloseAsync();
        Assert.True(await polEdit.IsUnsavedChangesDialogVisibleAsync());

        await polEdit.ConfirmSaveFromUnsavedChangesDialogAsync();

        Assert.EndsWith("/leave-policies", _page.Url);
        await polList.GoToAsync(AcmeId);
        Assert.True(await polList.HasItemAsync(polName));
    }

    [Fact]
    public async Task Close_CancelUnsavedChangesDialog_StaysOnPageWithFieldIntact()
    {
        var polName = $"E2E CancelClose {Guid.NewGuid().ToString("N")[..8]}";

        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var polEdit = new LeavePolicyEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await polEdit.GoToNewAsync(AcmeId);
        await polEdit.FillNameAsync(polName);

        await polEdit.ClickCloseAsync();
        Assert.True(await polEdit.IsUnsavedChangesDialogVisibleAsync());

        await polEdit.CancelUnsavedChangesDialogAsync();

        Assert.Contains("/leave-policies/new", _page.Url);
        Assert.Equal(polName, await polEdit.GetNameAsync());
    }
}
