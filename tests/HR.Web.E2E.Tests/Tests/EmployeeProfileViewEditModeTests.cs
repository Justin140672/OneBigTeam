using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the 2026-08 employee profile redesign: existing employees now open in a genuine
/// read-only view mode ("/{Id}/view") with disabled/readonly controls (not just CSS styling),
/// an "Edit details" button gated on Session.CanManageEmployees that drops into the editable
/// route, a sticky Save/Cancel action bar only in edit mode, an accessible save-success
/// confirmation, and Cancel/unsaved-changes-protection semantics around it. Also covers the
/// "Users &amp; Access" card rename/relocation and the Details tab's field label associations.
///
/// Every test creates its own fresh employee via the standard New Employee form (same pattern as
/// EmployeeLeavingProcessTests.CreateEmployeeAsync) so view/edit assertions never race concurrent
/// mutation of a shared seeded employee.
/// </summary>
public sealed class EmployeeProfileViewEditModeTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private const string LauraEmail = "laura.bennett@acme.example";

    private async Task<(Guid EmployeeId, string LastName)> CreateEmployeeAsync(
        EmployeeListPage empList, EmployeeEditPage empEdit, string suffix)
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var lastName = $"ProfileMode{suffix}{unique}";
        var workEmail = $"e2e.profilemode.{suffix.ToLowerInvariant()}{unique}@acme.example";

        await empList.GoToAsync(AcmeId);
        await empList.ClickNewEmployeeAsync();

        await empEdit.FillFirstNameAsync("E2E");
        await empEdit.FillLastNameAsync(lastName);
        await empEdit.FillWorkEmailAsync(workEmail);
        await empEdit.SelectDropdownAsync("Gender", "Male");
        await empEdit.SelectDropdownAsync("Nationality", "British");
        await empEdit.FillDateOfBirthAsync("15/06/1990");
        await empEdit.FillStartDateAsync("01/03/2026");
        await empEdit.FillEmployeeNumberAsync($"E2E-{unique}");
        await empEdit.SelectDropdownAsync("Employment Type", "Permanent");
        await empEdit.SelectDropdownAsync("Position Profile", "Senior Software Engineer");

        await empEdit.SaveNewEmployeeAsync();
        await empList.ClickEmployeeAsync(lastName);

        var match = Regex.Match(_page.Url, @"/employees/([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})");
        return (Guid.Parse(match.Groups[1].Value), lastName);
    }

    // ── View mode ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExistingEmployee_OpensInViewMode_ByDefault_FromEmployeeList()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await CreateEmployeeAsync(empList, empEdit, "Default");

        // ClickEmployeeAsync navigated us here via EmployeeList.razor's row link, which now
        // points at the "/view" route (see EmployeeList.razor's employee link builder).
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

        await CreateEmployeeAsync(empList, empEdit, "ReadOnly");

        Assert.True(empEdit.IsInViewModeUrl);

        // Genuinely disabled per the fix's own rationale (EmployeeEmploymentTab.razor's IsViewMode
        // comment: "pointer-events:none only prevented mouse interaction, not keyboard/programmatic
        // edits") — assert on the actual `readonly` HTML attribute, not visual/CSS state.
        Assert.True(await empEdit.IsTextFieldReadOnlyAsync("employee-first-name"),
            "Expected the First Name field to carry the HTML readonly attribute in view mode");
        Assert.True(await empEdit.IsTextFieldReadOnlyAsync("employee-work-email"),
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

        await CreateEmployeeAsync(empList, empEdit, "BackNav");

        await empEdit.ClickBackToEmployeesButtonAsync();

        Assert.EndsWith("/employees", _page.Url);
    }

    // ── Entering edit mode ───────────────────────────────────────────────────────

    [Fact]
    public async Task EditDetails_MakesControlsEditable_AndShowsStickyActionBar()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await CreateEmployeeAsync(empList, empEdit, "EnterEdit");
        Assert.True(empEdit.IsInViewModeUrl);

        await empEdit.ClickEditDetailsButtonAsync();

        Assert.False(empEdit.IsInViewModeUrl, $"Expected to leave the '/view' route after clicking Edit details, got: {_page.Url}");
        Assert.False(await empEdit.IsTextFieldReadOnlyAsync("employee-first-name"),
            "Expected the First Name field to no longer be readonly in edit mode");
        Assert.True(await empEdit.IsStickyActionBarVisibleAsync(),
            "Expected the sticky Save/Cancel action bar to render in edit mode");
        Assert.False(await empEdit.IsBackToEmployeesButtonVisibleAsync(),
            "'Back to employees' is a view-mode-only action");
    }

    // ── Save success confirmation ────────────────────────────────────────────────

    [Fact]
    public async Task Save_ShowsAccessibleSuccessConfirmation_ThenReturnsToViewModeWithUpdatedData()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await CreateEmployeeAsync(empList, empEdit, "Save");
        await empEdit.ClickEditDetailsButtonAsync();

        var newPreferredName = $"Preferred{Guid.NewGuid().ToString("N")[..6]}";
        await empEdit.FillTextFieldByIdAsync("employee-preferred-name", newPreferredName);

        // Click Save directly (sticky bar) rather than ClickSaveChangesAsync's spinner-wait, since
        // we need to catch the ~700ms success banner before OnSavedAsync's forceLoad navigates
        // away — race the banner check against the click instead of waiting for the spinner first.
        var saveClick = _page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();

        var banner = _page.Locator("[role='status'][aria-live='polite'].alert-success");
        await banner.WaitForAsync(new() { Timeout = 10_000 });
        var bannerText = (await banner.TextContentAsync())?.Trim();
        Assert.False(string.IsNullOrWhiteSpace(bannerText),
            "Expected a non-empty accessible (role=status, aria-live=polite) save confirmation banner");
        Assert.Contains("saved", bannerText, StringComparison.OrdinalIgnoreCase);

        await saveClick;

        // OnSavedAsync's forceLoad navigation lands back on the view route with fresh data.
        await _page.WaitForURLAsync(url => url.Contains("/view", StringComparison.OrdinalIgnoreCase), new() { Timeout = 20_000 });
        await _page.WaitForSelectorAsync("span[role='combobox']", new() { Timeout = 20_000 });

        Assert.True(empEdit.IsInViewModeUrl);
        Assert.Equal(newPreferredName, await empEdit.GetTextFieldValueAsync("employee-preferred-name"));
    }

    // ── Cancel discards edits ────────────────────────────────────────────────────

    [Fact]
    public async Task Cancel_DiscardsEdit_AndReturnsToViewMode_ShowingOriginalValue()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await CreateEmployeeAsync(empList, empEdit, "Cancel");

        var originalValue = await empEdit.GetTextFieldValueAsync("employee-preferred-name");

        await empEdit.ClickEditDetailsButtonAsync();
        await empEdit.FillTextFieldByIdAsync("employee-preferred-name", "ShouldBeDiscarded");

        await empEdit.ClickCancelEditButtonAsync();

        Assert.True(empEdit.IsInViewModeUrl, $"Expected Cancel to return to the view route, got: {_page.Url}");
        var reloadedValue = await empEdit.GetTextFieldValueAsync("employee-preferred-name");
        Assert.Equal(originalValue, reloadedValue);
        Assert.NotEqual("ShouldBeDiscarded", reloadedValue);
    }

    // ── Unsaved-changes protection while editing ────────────────────────────────

    [Fact]
    public async Task UnsavedChanges_NavigatingViaBreadcrumb_ShowsConfirmDialog_AndDiscardCorrectlyDiscards()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await CreateEmployeeAsync(empList, empEdit, "Breadcrumb");
        await empEdit.ClickEditDetailsButtonAsync();

        await empEdit.FillTextFieldByIdAsync("employee-preferred-name", "UnsavedBreadcrumbEdit");

        // SfBreadcrumb's "Employees" item — an in-app navigation attempt intercepted by
        // EditPageBase's HandleLocationChangingAsync while the model is dirty.
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

        await CreateEmployeeAsync(empList, empEdit, "BreadcrumbCancel");
        await empEdit.ClickEditDetailsButtonAsync();

        await empEdit.FillTextFieldByIdAsync("employee-preferred-name", "StillPendingEdit");

        await _page.GetByRole(AriaRole.Link, new() { Name = "Employees", Exact = true }).ClickAsync();
        Assert.True(await empEdit.IsUnsavedChangesDialogVisibleAsync());

        await empEdit.CancelUnsavedChangesDialogAsync();

        // Still on the edit route with the in-progress edit intact.
        Assert.False(empEdit.IsInViewModeUrl);
        Assert.Equal("StillPendingEdit", await empEdit.GetTextFieldValueAsync("employee-preferred-name"));
    }

    // ── Users & Access card ──────────────────────────────────────────────────────

    [Fact]
    public async Task UsersAndAccessCard_IsSeparateFromPersonalInfo_WithSignInCheckboxAndInviteExpiryNote()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await CreateEmployeeAsync(empList, empEdit, "UsersAccess");
        await empEdit.ClickEditDetailsButtonAsync();

        Assert.True(await empEdit.IsUsersAndAccessCardVisibleAsync(),
            "Expected a 'Users & Access' card, structurally separated from Personal Information");

        var checkbox = _page.GetByLabel("Allow this employee to sign in.");
        await Assertions.Expect(checkbox).ToBeVisibleAsync(new() { Timeout = 10_000 });

        await checkbox.CheckAsync();
        Assert.True(await empEdit.HasInviteExpiryNoteAsync(),
            "Expected explanatory text about the 7-day invite link expiry once system access is enabled");
    }

    // ── Accessibility / labels ───────────────────────────────────────────────────

    [Fact]
    public async Task DetailsTab_HasRequiredFieldsNote_AndLabelsAreAccessiblyAssociated()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await CreateEmployeeAsync(empList, empEdit, "A11y");
        await empEdit.ClickEditDetailsButtonAsync();

        Assert.True(await empEdit.HasRequiredFieldsNoteAsync(),
            "Expected the 'Fields marked * are required.' explanatory note on the Details tab");

        // A sample of fields whose <label for="..."> was newly associated with the underlying
        // input's id (see EmployeeEdit.razor's employee-first-name/employee-work-email/etc ids).
        // GetByLabel resolves via the accessible name computed from that association, proving it's
        // real (not just visual proximity).
        foreach (var (labelText, expectFieldId) in new[]
                 {
                     ("First Name", "employee-first-name"),
                     ("Work Email", "employee-work-email"),
                     ("City", "employee-city"),
                 })
        {
            var field = _page.GetByLabel(labelText).First;
            await Assertions.Expect(field).ToBeVisibleAsync(new() { Timeout = 10_000 });
            var id = await field.GetAttributeAsync("id");
            Assert.Equal(expectFieldId, id);
        }
    }

    // ── Keyboard operability ─────────────────────────────────────────────────────

    [Fact]
    public async Task EditDetailsButton_IsKeyboardOperable()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await CreateEmployeeAsync(empList, empEdit, "Kbd");
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

        await CreateEmployeeAsync(empList, empEdit, "KbdMenu");

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

        await CreateEmployeeAsync(empList, empEdit, "KbdTabs");

        var detailsTab = _page.GetByRole(AriaRole.Tab, new() { Name = "Details" });
        await detailsTab.FocusAsync();
        await Assertions.Expect(detailsTab).ToBeFocusedAsync(new() { Timeout = 5_000 });

        // Syncfusion's SfTab uses a roving-tabindex arrow-key model (not sequential Tab) to move
        // between tabs within the tablist, matching standard ARIA tabs pattern.
        await _page.Keyboard.PressAsync("ArrowRight");

        var employmentTab = _page.GetByRole(AriaRole.Tab, new() { Name = "Employment" });
        await Assertions.Expect(employmentTab).ToBeFocusedAsync(new() { Timeout = 5_000 });

        await _page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(employmentTab).ToHaveAttributeAsync("aria-selected", "true", new() { Timeout = 10_000 });
    }
}
