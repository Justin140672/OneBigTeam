using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Direct coverage of the Close / unsaved-changes prompt (EditPageBase) on the Leave Type
/// edit page.
/// </summary>
public sealed class LeaveTypeEditCloseBehaviorTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    private static string RandomCode() => $"E2E{Guid.NewGuid().ToString("N")[..5].ToUpperInvariant()}";

    [Fact]
    public async Task Close_ExistingRecordWithNoChanges_NavigatesDirectlyToList()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var typeList = new LeaveTypeListPage(_page, _fixture.WebBaseUrl);
        var typeEdit = new LeaveTypeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var typeName = $"E2E Close {Guid.NewGuid().ToString("N")[..8]}";
        await typeList.GoToAsync(AcmeId);
        await typeList.ClickNewAsync();
        await typeEdit.FillNameAsync(typeName);
        await typeEdit.FillCodeAsync(RandomCode());
        await typeEdit.SaveAsync();

        await typeList.GoToAsync(AcmeId);
        var href = await _page.Locator(".e-rowcell a").Filter(new() { HasText = typeName }).First.GetAttributeAsync("href");
        Assert.NotNull(href);
        await _page.GotoAsync($"{_fixture.WebBaseUrl}{href}");
        await _page.WaitForSelectorAsync("button:has-text('Save')", new() { Timeout = 20_000 });

        await typeEdit.CloseAndWaitForListAsync();

        Assert.EndsWith("/leave-types", _page.Url);
    }

    [Fact]
    public async Task Close_NewRecordWithUnsavedChanges_ShowsConfirmDialog()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var typeEdit = new LeaveTypeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await typeEdit.GoToNewAsync(AcmeId);
        await typeEdit.FillNameAsync("Unsaved Leave Type");

        await typeEdit.ClickCloseAsync();

        Assert.True(await typeEdit.IsUnsavedChangesDialogVisibleAsync());
        Assert.Contains("/leave-types/new", _page.Url);
    }

    [Fact]
    public async Task Close_DiscardChanges_NavigatesAwayWithoutSaving()
    {
        var typeName = $"E2E Discard {Guid.NewGuid().ToString("N")[..8]}";

        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var typeList = new LeaveTypeListPage(_page, _fixture.WebBaseUrl);
        var typeEdit = new LeaveTypeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await typeEdit.GoToNewAsync(AcmeId);
        await typeEdit.FillNameAsync(typeName);
        await typeEdit.FillCodeAsync(RandomCode());

        await typeEdit.ClickCloseAsync();
        Assert.True(await typeEdit.IsUnsavedChangesDialogVisibleAsync());

        await typeEdit.ConfirmDiscardChangesAsync();

        Assert.EndsWith("/leave-types", _page.Url);
        await typeList.GoToAsync(AcmeId);
        Assert.False(await typeList.HasItemAsync(typeName));
    }

    [Fact]
    public async Task Close_SaveFromUnsavedChangesDialog_SavesAndNavigatesToList()
    {
        var typeName = $"E2E SaveOnClose {Guid.NewGuid().ToString("N")[..8]}";

        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var typeList = new LeaveTypeListPage(_page, _fixture.WebBaseUrl);
        var typeEdit = new LeaveTypeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await typeEdit.GoToNewAsync(AcmeId);
        await typeEdit.FillNameAsync(typeName);
        await typeEdit.FillCodeAsync(RandomCode());

        await typeEdit.ClickCloseAsync();
        Assert.True(await typeEdit.IsUnsavedChangesDialogVisibleAsync());

        await typeEdit.ConfirmSaveFromUnsavedChangesDialogAsync();

        Assert.EndsWith("/leave-types", _page.Url);
        await typeList.GoToAsync(AcmeId);
        Assert.True(await typeList.HasItemAsync(typeName));
    }

    [Fact]
    public async Task Close_CancelUnsavedChangesDialog_StaysOnPageWithFieldIntact()
    {
        var typeName = $"E2E CancelClose {Guid.NewGuid().ToString("N")[..8]}";

        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var typeEdit = new LeaveTypeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await typeEdit.GoToNewAsync(AcmeId);
        await typeEdit.FillNameAsync(typeName);

        await typeEdit.ClickCloseAsync();
        Assert.True(await typeEdit.IsUnsavedChangesDialogVisibleAsync());

        await typeEdit.CancelUnsavedChangesDialogAsync();

        Assert.Contains("/leave-types/new", _page.Url);
        Assert.Equal(typeName, await typeEdit.GetNameAsync());
    }
}
