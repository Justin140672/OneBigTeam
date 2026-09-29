using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class EmployeeProfileViewEditModeTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private const string LauraEmail = "laura.bennett@acme.example";

    private static readonly SemaphoreSlim _sharedEmployeeLock = new(1, 1);
    private static (Guid EmployeeId, string LastName)? _sharedEmployee;

    private async Task<(Guid EmployeeId, string LastName)> GetSharedEmployeeAsync(
        EmployeeListPage empList, EmployeeEditPage empEdit)
    {
        if (_sharedEmployee is { } cached)
        {
            await empEdit.GoToViewAsync(AcmeId, cached.EmployeeId);
            return cached;
        }

        await _sharedEmployeeLock.WaitAsync();
        try
        {
            if (_sharedEmployee is { } cachedAfterLock)
            {
                await empEdit.GoToViewAsync(AcmeId, cachedAfterLock.EmployeeId);
                return cachedAfterLock;
            }

            var created = await CreateEmployeeAsync(empList, empEdit, "Shared");
            _sharedEmployee = created;
            return created;
        }
        finally
        {
            _sharedEmployeeLock.Release();
        }
    }

    private async Task<(Guid EmployeeId, string LastName)> CreateEmployeeAsync(
        EmployeeListPage empList, EmployeeEditPage empEdit, string suffix)
    {
        _ = empList;
        _ = suffix;
        var seeded = SeededE2eEmployees.ProfileViewEditMode;
        await empEdit.GoToViewAsync(AcmeId, seeded.EmployeeId);
        return (seeded.EmployeeId, seeded.LastName);
    }


    [Fact]
    public async Task ExistingEmployee_OpensInViewMode_ByDefault_FromEmployeeList()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await GetSharedEmployeeAsync(empList, empEdit);

        Assert.True(empEdit.IsInViewModeUrl, $"Expected to land on the '/view' route, got: {_page.Url}");
        Assert.True(await empEdit.IsEditDetailsButtonVisibleAsync(),
            "Expected the 'Edit details' button to be visible in view mode for an HR administrator");
        Assert.True(await empEdit.IsBackToEmployeesButtonVisibleAsync(),
            "Expected the 'Back to employees' button in view mode");
        Assert.False(await empEdit.IsStickyActionBarVisibleAsync(),
            "The sticky Save/Cancel action bar should not render in view mode");
    }

    [Fact]
    public async Task ViewMode_TextFieldsAreGenuinelyReadOnly_NotJustStyled()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await GetSharedEmployeeAsync(empList, empEdit);

        Assert.True(empEdit.IsInViewModeUrl);

        Assert.True(await empEdit.IsTextFieldReadOnlyAsync("First Name"),
            "Expected the First Name field to carry the HTML readonly attribute in view mode");
        Assert.True(await empEdit.IsTextFieldReadOnlyAsync("Work Email"),
            "Expected the Work Email field to carry the HTML readonly attribute in view mode");
    }

    [Fact]
    public async Task BackToEmployees_NavigatesToEmployeeList()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await GetSharedEmployeeAsync(empList, empEdit);

        await empEdit.ClickBackToEmployeesButtonAsync();

        Assert.EndsWith("/employees", _page.Url);
    }


    [Fact]
    public async Task EditDetails_MakesControlsEditable_AndShowsStickyActionBar()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await GetSharedEmployeeAsync(empList, empEdit);
        Assert.True(empEdit.IsInViewModeUrl);

        await empEdit.ClickEditDetailsButtonAsync();

        Assert.False(empEdit.IsInViewModeUrl, $"Expected to leave the '/view' route after clicking Edit details, got: {_page.Url}");
        Assert.False(await empEdit.IsTextFieldReadOnlyAsync("First Name"),
            "Expected the First Name field to no longer be readonly in edit mode");
        Assert.True(await empEdit.IsStickyActionBarVisibleAsync(),
            "Expected the sticky Save/Cancel action bar to render in edit mode");
        Assert.False(await empEdit.IsBackToEmployeesButtonVisibleAsync(),
            "'Back to employees' is a view-mode-only action");
    }


    [Fact]
    public async Task Save_ShowsAccessibleSuccessConfirmation_ThenReturnsToViewModeWithUpdatedData()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await GetSharedEmployeeAsync(empList, empEdit);
        await empEdit.ClickEditDetailsButtonAsync();

        var newPreferredName = $"Preferred{Guid.NewGuid().ToString("N")[..6]}";
        await empEdit.FillTextFieldByIdAsync("Preferred Name", newPreferredName);

        var saveClick = _page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();

        var banner = _page.Locator("[role='status'][aria-live='polite'].alert-success");
        await banner.WaitForAsync(new() { Timeout = 10_000 });
        var bannerText = (await banner.TextContentAsync())?.Trim();
        Assert.False(string.IsNullOrWhiteSpace(bannerText),
            "Expected a non-empty accessible (role=status, aria-live=polite) save confirmation banner");
        Assert.Contains("saved", bannerText, StringComparison.OrdinalIgnoreCase);

        await saveClick;

        await _page.WaitForURLAsync(url => url.Contains("/view", StringComparison.OrdinalIgnoreCase), new() { Timeout = 40_000 });
        await _page.WaitForSelectorAsync(".content-area span[role='combobox']", new() { Timeout = 20_000 });

        Assert.True(empEdit.IsInViewModeUrl);
        Assert.Equal(newPreferredName, await empEdit.GetTextFieldValueAsync("Preferred Name"));
    }


    [Fact]
    public async Task Cancel_WithUnsavedChanges_ShowsConfirmDialog_AndDiscardReturnsOriginalValue()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        var (employeeId, _) = await GetSharedEmployeeAsync(empList, empEdit);

        var originalValue = await empEdit.GetTextFieldValueAsync("Preferred Name");

        await empEdit.ClickEditDetailsButtonAsync();
        await empEdit.FillTextFieldByIdAsync("Preferred Name", "ShouldBeDiscarded");

        await empEdit.ClickCloseAsync();
        Assert.True(await empEdit.IsUnsavedChangesDialogVisibleAsync(),
            "Expected the unsaved-changes confirmation dialog after clicking Cancel with a pending edit");

        await empEdit.ConfirmDiscardChangesAsync();
        Assert.EndsWith("/employees", _page.Url);

        await empEdit.GoToViewAsync(AcmeId, employeeId);
        var reloadedValue = await empEdit.GetTextFieldValueAsync("Preferred Name");
        Assert.Equal(originalValue, reloadedValue);
        Assert.NotEqual("ShouldBeDiscarded", reloadedValue);
    }


    [Fact]
    public async Task UnsavedChanges_NavigatingViaBreadcrumb_ShowsConfirmDialog_AndDiscardCorrectlyDiscards()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await GetSharedEmployeeAsync(empList, empEdit);
        await empEdit.ClickEditDetailsButtonAsync();

        await empEdit.FillTextFieldByIdAsync("Preferred Name", "UnsavedBreadcrumbEdit");

        await _page.GetByRole(AriaRole.Link, new() { Name = "Employees", Exact = true }).ClickAsync();

        Assert.True(await empEdit.IsUnsavedChangesDialogVisibleAsync(),
            "Expected the unsaved-changes confirmation dialog when navigating away via the breadcrumb with edits pending");

        await empEdit.ConfirmDiscardChangesAsync();
        Assert.EndsWith("/employees", _page.Url);
    }

    [Fact]
    public async Task UnsavedChanges_CancellingTheDialog_StaysOnEditPage_WithEditStillPending()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await GetSharedEmployeeAsync(empList, empEdit);
        await empEdit.ClickEditDetailsButtonAsync();

        await empEdit.FillTextFieldByIdAsync("Preferred Name", "StillPendingEdit");

        await _page.GetByRole(AriaRole.Link, new() { Name = "Employees", Exact = true }).ClickAsync();
        Assert.True(await empEdit.IsUnsavedChangesDialogVisibleAsync());

        await empEdit.CancelUnsavedChangesDialogAsync();

        Assert.False(empEdit.IsInViewModeUrl);
        Assert.Equal("StillPendingEdit", await empEdit.GetTextFieldValueAsync("Preferred Name"));
    }


    [Fact]
    public async Task DetailsTab_HasRequiredFieldsNote_AndLabelsAreAccessiblyAssociated()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await GetSharedEmployeeAsync(empList, empEdit);
        await empEdit.ClickEditDetailsButtonAsync();

        Assert.True(await empEdit.HasRequiredFieldsNoteAsync(),
            "Expected the 'Fields marked * are required.' explanatory note on the Details tab");

        foreach (var labelText in new[] { "First Name", "Work Email", "City" })
        {
            var field = _page.GetByLabel(labelText).First;
            await Assertions.Expect(field).ToBeVisibleAsync(new() { Timeout = 10_000 });
        }
    }


    [Fact]
    public async Task EditDetailsButton_IsKeyboardOperable()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await GetSharedEmployeeAsync(empList, empEdit);
        Assert.True(empEdit.IsInViewModeUrl);

        var editButton = _page.Locator("[data-testid='edit-details-button']");
        await editButton.FocusAsync();
        await Assertions.Expect(editButton).ToBeFocusedAsync(new() { Timeout = 5_000 });

        await _page.Keyboard.PressAsync("Enter");

        await _page.WaitForURLAsync(url => !url.Contains("/view", StringComparison.OrdinalIgnoreCase), new() { Timeout = 20_000 });
        Assert.False(empEdit.IsInViewModeUrl,
            "Expected pressing Enter on the focused 'Edit details' button to enter edit mode");
    }

    [Fact]
    public async Task MoreActionsMenu_IsKeyboardOperable_OpensAndCloses()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await GetSharedEmployeeAsync(empList, empEdit);

        var moreActions = _page.GetByRole(AriaRole.Button, new() { Name = "More actions" });
        await moreActions.FocusAsync();
        await Assertions.Expect(moreActions).ToBeFocusedAsync(new() { Timeout = 5_000 });

        await _page.Keyboard.PressAsync("Enter");

        var orgChartItem = _page.GetByRole(AriaRole.Menuitem, new() { Name = "View Organisation Chart" });
        await Assertions.Expect(orgChartItem).ToBeVisibleAsync(new() { Timeout = 10_000 });

        await _page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(orgChartItem).ToBeHiddenAsync(new() { Timeout = 10_000 });
    }

    [Fact]
    public async Task TabStrip_IsReachableAndOperableViaKeyboard()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await GetSharedEmployeeAsync(empList, empEdit);

        await EmployeeEditPage.SelectOwningGroupAsync(_page, "Details");
        var detailsTab = EmployeeEditPage.SectionTab(_page, "Details");
        await detailsTab.FocusAsync();
        await Assertions.Expect(detailsTab).ToBeFocusedAsync(new() { Timeout = 5_000 });

        await _page.Keyboard.PressAsync("ArrowRight");

        var employmentTab = EmployeeEditPage.SectionTab(_page, "Employment");
        await Assertions.Expect(employmentTab).ToBeFocusedAsync(new() { Timeout = 5_000 });

        await _page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(employmentTab).ToHaveAttributeAsync("aria-selected", "true", new() { Timeout = 10_000 });
    }
}
