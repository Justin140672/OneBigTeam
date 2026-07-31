using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Direct coverage of the Close / unsaved-changes prompt on the Employee edit page.
/// EmployeeEdit is a non-trivial host for this shared EditPageBase behavior: it owns its own
/// model (the Details tab) but also overrides <c>HasUnsavedChanges</c> to fold in the
/// Employment tab's own independently-saved model — so an edit made purely on the Employment
/// tab must still trigger the Close prompt, and Discard/Save from that prompt must cover both.
/// </summary>
[Collection("E2E")]
public sealed class EmployeeEditCloseBehaviorTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId  = Guid.Parse("00000000-0000-0000-0000-000000000001");
    // Marcus Diallo — seeded HR Advisor with no manager set (also used by AssignManagerTests).
    private static readonly Guid MarcusId = Guid.Parse("30000000-0000-0000-0000-000000000006");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task Close_NewEmployeeWithNoChanges_NavigatesDirectlyToList()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToNewAsync(AcmeId);

        await empEdit.ClickCloseAsync();
        await _page.WaitForURLAsync("**/employees", new() { Timeout = 15_000 });
        await _page.WaitForSelectorAsync(".e-grid", new() { Timeout = 20_000 });

        Assert.EndsWith("/employees", _page.Url);
    }

    [Fact]
    public async Task Close_NewEmployeeWithUnsavedChanges_ShowsConfirmDialog()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToNewAsync(AcmeId);
        await empEdit.FillFirstNameAsync("Unsaved");

        await empEdit.ClickCloseAsync();

        Assert.True(await empEdit.IsUnsavedChangesDialogVisibleAsync(),
            "Expected the unsaved-changes confirmation dialog when closing a new-employee form with edits pending");
        Assert.Contains("/employees/new", _page.Url);
    }

    [Fact]
    public async Task Close_ExistingEmployee_EmploymentTabEditOnly_StillShowsConfirmDialog()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, MarcusId);
        await empEdit.OpenEmploymentTabAsync();

        // Edit a field that lives on the Employment tab's own model, not the Details tab's —
        // this is what exercises EmployeeEdit's HasUnsavedChanges override.
        await empEdit.FillEmployeeNumberAsync($"E2E-{Guid.NewGuid().ToString("N")[..6]}");

        await empEdit.ClickCloseAsync();

        Assert.True(await empEdit.IsUnsavedChangesDialogVisibleAsync(),
            "Expected the unsaved-changes dialog to appear for an edit made only on the Employment tab");
    }

    [Fact]
    public async Task Close_DiscardEmploymentTabChanges_NavigatesAwayWithoutSaving()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, MarcusId);
        await empEdit.OpenEmploymentTabAsync();

        var originalNumber = await _page.GetByPlaceholder("e.g. EMP-001").InputValueAsync();
        var discardedNumber = $"E2E-DISCARD-{Guid.NewGuid().ToString("N")[..6]}";
        await empEdit.FillEmployeeNumberAsync(discardedNumber);

        await empEdit.ClickCloseAsync();
        Assert.True(await empEdit.IsUnsavedChangesDialogVisibleAsync());

        await empEdit.ConfirmDiscardChangesAsync();
        Assert.EndsWith("/employees", _page.Url);

        // Reload the employee and confirm the discarded value never persisted.
        await empEdit.GoToAsync(AcmeId, MarcusId);
        await empEdit.OpenEmploymentTabAsync();
        var reloadedNumber = await _page.GetByPlaceholder("e.g. EMP-001").InputValueAsync();
        Assert.Equal(originalNumber, reloadedNumber);
        Assert.NotEqual(discardedNumber, reloadedNumber);
    }

    [Fact]
    public async Task Close_SaveFromUnsavedChangesDialog_PersistsEmploymentTabChange()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, MarcusId);
        await empEdit.OpenEmploymentTabAsync();

        var newNumber = $"E2E-SAVE-{Guid.NewGuid().ToString("N")[..6].ToUpper()}";
        await empEdit.FillEmployeeNumberAsync(newNumber);

        await empEdit.ClickCloseAsync();
        Assert.True(await empEdit.IsUnsavedChangesDialogVisibleAsync());

        await empEdit.ConfirmSaveFromUnsavedChangesDialogAsync();
        Assert.EndsWith("/employees", _page.Url);

        await empEdit.GoToAsync(AcmeId, MarcusId);
        await empEdit.OpenEmploymentTabAsync();
        Assert.Equal(newNumber, await _page.GetByPlaceholder("e.g. EMP-001").InputValueAsync());
    }
}
