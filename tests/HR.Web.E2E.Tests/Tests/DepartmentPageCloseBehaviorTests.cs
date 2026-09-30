using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

public sealed class DepartmentPageCloseBehaviorTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
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

        var deptName = $"E2E Close {Guid.NewGuid().ToString("N")[..8]}";
        await deptEdit.FillNameAsync(deptName);
        await deptEdit.SaveAsync();

        await deptList.GoToAsync(AcmeId);
        Assert.True(await deptList.HasDepartmentAsync(deptName));

        await _page.RevealGridRowAsync(deptName);
        var cells = await _page.Locator(".e-rowcell a").Filter(new() { HasText = deptName }).First.GetAttributeAsync("href");
        Assert.NotNull(cells);
        await _page.GotoAsync($"{_fixture.WebBaseUrl}{cells}");
        await _page.WaitForSelectorAsync(".content-area span[role='combobox']", new() { Timeout = 20_000 });

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

        Assert.Contains("/departments/new", _page.Url);
        Assert.Equal(deptName, await deptEdit.GetNameAsync());
    }
}
