using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the Close button and "unsaved changes" confirmation prompt that <c>EditPageBase</c>
/// provides to every edit page (see EditPageBase.cs / UnsavedChangesDialog.razor). Exercised
/// via the Department edit page as a representative host — the behavior under test lives in
/// the shared base class, not in DepartmentEdit itself.
/// </summary>
[Collection("E2E")]
public sealed class DepartmentPageCloseBehaviorTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task Close_ExistingRecordWithNoChanges_NavigatesDirectlyToList()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var deptList = new DepartmentListPage(_page, _fixture.WebBaseUrl);
        var deptEdit = new DepartmentEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await deptList.GoToAsync(AcmeId);
        await deptList.ClickNewDepartmentAsync();

        // Create a department first so we have an existing, unmodified record to reopen.
        var deptName = $"E2E Close {Guid.NewGuid().ToString("N")[..8]}";
        await deptEdit.FillNameAsync(deptName);
        await deptEdit.SaveAsync();

        await deptList.GoToAsync(AcmeId);
        Assert.True(await deptList.HasDepartmentAsync(deptName));

        // Reopening it and clicking Close with no edits should navigate straight back to the
        // list — no "unsaved changes" prompt should appear (the wait inside CloseAndWaitForListAsync
        // would time out if one blocked navigation).
        var cells = await _page.Locator(".e-rowcell a").Filter(new() { HasText = deptName }).First.GetAttributeAsync("href");
        Assert.NotNull(cells);
        await _page.GotoAsync($"{_fixture.WebBaseUrl}{cells}");
        await _page.WaitForSelectorAsync("span[role='combobox']", new() { Timeout = 20_000 });

        await deptEdit.CloseAndWaitForListAsync();

        Assert.EndsWith("/departments", _page.Url);
    }

    [Fact]
    public async Task Close_NewRecordWithUnsavedChanges_ShowsConfirmDialog()
    {
        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var deptEdit = new DepartmentEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await deptEdit.GoToNewAsync(AcmeId);
        await deptEdit.FillNameAsync("Unsaved Department Name");

        await deptEdit.ClickCloseAsync();

        Assert.True(await deptEdit.IsUnsavedChangesDialogVisibleAsync(),
            "Expected the unsaved-changes confirmation dialog to appear when closing with edits pending");
        Assert.Contains("/departments/new", _page.Url);
    }

    [Fact]
    public async Task Close_DiscardChanges_NavigatesAwayWithoutSaving()
    {
        var deptName = $"E2E Discard {Guid.NewGuid().ToString("N")[..8]}";

        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var deptList = new DepartmentListPage(_page, _fixture.WebBaseUrl);
        var deptEdit = new DepartmentEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await deptEdit.GoToNewAsync(AcmeId);
        await deptEdit.FillNameAsync(deptName);

        await deptEdit.ClickCloseAsync();
        Assert.True(await deptEdit.IsUnsavedChangesDialogVisibleAsync());

        await deptEdit.ConfirmDiscardChangesAsync();

        Assert.EndsWith("/departments", _page.Url);

        await deptList.GoToAsync(AcmeId);
        Assert.False(await deptList.HasDepartmentAsync(deptName),
            "Discarding changes should not have created the department");
    }

    [Fact]
    public async Task Close_SaveFromUnsavedChangesDialog_SavesAndNavigatesToList()
    {
        var deptName = $"E2E SaveOnClose {Guid.NewGuid().ToString("N")[..8]}";

        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var deptList = new DepartmentListPage(_page, _fixture.WebBaseUrl);
        var deptEdit = new DepartmentEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await deptEdit.GoToNewAsync(AcmeId);
        await deptEdit.FillNameAsync(deptName);

        await deptEdit.ClickCloseAsync();
        Assert.True(await deptEdit.IsUnsavedChangesDialogVisibleAsync());

        await deptEdit.ConfirmSaveFromUnsavedChangesDialogAsync();

        Assert.EndsWith("/departments", _page.Url);
        Assert.True(await deptList.HasDepartmentAsync(deptName),
            "Choosing Save from the unsaved-changes dialog should have created the department");
    }

    [Fact]
    public async Task Close_CancelUnsavedChangesDialog_StaysOnPageWithFieldIntact()
    {
        var deptName = $"E2E CancelClose {Guid.NewGuid().ToString("N")[..8]}";

        var login    = new LoginPage(_page, _fixture.WebBaseUrl);
        var deptEdit = new DepartmentEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await deptEdit.GoToNewAsync(AcmeId);
        await deptEdit.FillNameAsync(deptName);

        await deptEdit.ClickCloseAsync();
        Assert.True(await deptEdit.IsUnsavedChangesDialogVisibleAsync());

        await deptEdit.CancelUnsavedChangesDialogAsync();

        // Cancelling the prompt should just dismiss it — the user stays on the form with
        // their edits untouched, free to keep editing or click Close again.
        Assert.Contains("/departments/new", _page.Url);
        Assert.Equal(deptName, await deptEdit.GetNameAsync());
    }
}
