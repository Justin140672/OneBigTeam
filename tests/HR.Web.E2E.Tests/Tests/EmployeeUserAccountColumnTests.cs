using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the "User Account" status column and row-level "Invite User" Quick Invite action added
/// to the Employee List (tickets #90/#91) — closing the E2E coverage gap tracked by ticket #96.
///
/// The redesigned column (commit a80960cc) renders three-state display labels — "Active",
/// "Disabled", "No account" (was "No User"), "Invited" (was "Pending Invitation"), "Invite
/// expired" (was "Invitation Expired") — and the old inline "Invite User" row link is now an
/// "Invite" item in a per-row ⋮ actions menu. The column stays sortable and Excel-filterable
/// (the filter lists the raw UserAccountStatus values, e.g. "No User").
///
/// Personas/employees used (all seeded Acme data, same convention as
/// UserAdministrationManagementTests):
/// - "Laura Bennett" — HR Administrator, seeded active dev-persona account -> "Active".
/// - "Carlos Rivera" — seeded active dev-persona account, used (and restored) as the "Disabled"
///   arrangement so as not to permanently disable a persona another test might rely on.
/// - "Sophie Laurent" — seeded Acme employee with no corresponding dev-persona user account (see
///   EmployeesModule's MakeAcme seed list vs. IdentityModule.SeedDevUserAsync's persona list) ->
///   "No account". Only ever READ here (never invited) — the invite-mutation test below
///   (QuickInvite_ForNoUserEmployee...) creates its own fresh employee instead; see
///   CreateFreshUninvitedEmployeeAsync's doc comment for why.
///
/// "Invite expired" is not covered here: reaching that state requires a genuinely expired
/// invitation (time-based), which can't be arranged through the UI within a single test run
/// without directly manipulating the database — doing so would violate this suite's "don't fake
/// what can't genuinely be verified" convention. The icon/label mapping for it lives in
/// EmployeeList.AccountStateDisplay alongside the other four covered here.
/// </summary>
public sealed class EmployeeUserAccountColumnTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string HrAdminEmail = "laura.bennett@acme.example";

    private const string ActiveEmployeeName = "Laura Bennett";
    private const string DisableTargetEmployeeName = "Carlos Rivera";
    private const string NoUserEmployeeName = "Laurent";

    [Fact]
    public async Task UserAccountColumn_ShowsActiveIconAndLabel_ForEmployeeWithActiveAccount()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var list = new EmployeeListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);
        await list.GoToAsync(AcmeId);

        var text = await list.GetUserAccountStatusTextAsync(ActiveEmployeeName);
        Assert.Contains("Active", text);

        var iconClass = await list.GetUserAccountStatusIconClassAsync(ActiveEmployeeName);
        Assert.Contains("fa-circle-check", iconClass);
    }

    [Fact]
    public async Task UserAccountColumn_ShowsNoUserIconLabelAndInviteLink_ForEmployeeWithoutAccount()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var list = new EmployeeListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);
        await list.GoToAsync(AcmeId);

        var text = await list.GetUserAccountStatusTextAsync(NoUserEmployeeName);
        Assert.Contains("No account", text);

        var iconClass = await list.GetUserAccountStatusIconClassAsync(NoUserEmployeeName);
        Assert.Contains("fa-circle-minus", iconClass);

        Assert.True(await list.HasInviteUserLinkAsync(NoUserEmployeeName),
            "Expected the 'Invite' action on a row with no linked user account");
    }

    [Fact]
    public async Task UserAccountColumn_InviteLink_NotShown_ForEmployeeWithActiveAccount()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var list = new EmployeeListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);
        await list.GoToAsync(AcmeId);

        Assert.False(await list.HasInviteUserLinkAsync(ActiveEmployeeName),
            "The 'Invite' action should only be offered for 'No account' rows, not Active ones");
    }

    [Fact]
    public async Task UserAccountColumn_ShowsDisabledIconAndLabel_ForDisabledAccount()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var list = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var userAdminList = new UserAdministrationListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        // Arrange: disable Carlos Rivera's account via User Administration (same action covered
        // by UserAdministrationManagementTests.DisableThenEnableAccount_UpdatesAccountStatus),
        // then restore it in the finally block so no other test/persona is left locked out.
        await userAdminList.GoToAsync(AcmeId);
        await userAdminList.OpenUserDetailAsync(DisableTargetEmployeeName);
        var detail = new UserDetailPage(_page, _fixture.WebBaseUrl);
        Assert.Equal("Active", await detail.GetAccountStatusAsync());
        await detail.DisableAccountAsync();
        Assert.Equal("Disabled", await detail.GetAccountStatusAsync());

        try
        {
            await list.GoToAsync(AcmeId);

            var text = await list.GetUserAccountStatusTextAsync(DisableTargetEmployeeName);
            Assert.Contains("Disabled", text);

            var iconClass = await list.GetUserAccountStatusIconClassAsync(DisableTargetEmployeeName);
            Assert.Contains("fa-ban", iconClass);

            Assert.False(await list.HasInviteUserLinkAsync(DisableTargetEmployeeName),
                "The 'Invite' action should not be offered for a Disabled row");
        }
        finally
        {
            await userAdminList.GoToAsync(AcmeId);
            await userAdminList.OpenUserDetailAsync(DisableTargetEmployeeName);
            if (await detail.GetAccountStatusAsync() == "Disabled")
                await detail.EnableAccountAsync();
        }
    }

    // NOTE: column sorting and Excel-style filtering on the "User Account" column are not covered
    // here — those are Syncfusion grid behaviours, not our own logic, and asserting them just
    // tests the third-party control.

    [Fact]
    public async Task ExportButton_IsPresentAndEnabled_WithUserAccountColumnInGrid()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var list = new EmployeeListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);
        await list.GoToAsync(AcmeId);

        // The "User Account" column must actually be part of the grid the export button targets.
        Assert.True(await _page.Locator(".e-headercell").Filter(new() { HasText = "User Account" }).IsVisibleAsync());

        // EmployeeList.razor's EmployeeToolbar now folds the base toolbar's separate Print/
        // Export/Columns buttons into a single "More" overflow menu (OverflowActionsMenu,
        // rendered via OverflowMenuTemplate) — there is no longer a standalone "Export" button to
        // find directly. "Export to Excel/CSV/PDF" are items inside that dropdown instead.
        var moreButton = _page.GetByRole(AriaRole.Button, new() { Name = "More actions" });
        Assert.True(await moreButton.IsVisibleAsync());
        Assert.False(await moreButton.IsDisabledAsync());

        await moreButton.ClickAsync();
        var exportToExcelItem = _page.GetByRole(AriaRole.Menuitem, new() { Name = "Export to Excel" });
        await exportToExcelItem.WaitForAsync(new() { Timeout = 10_000 });
        Assert.True(await exportToExcelItem.IsVisibleAsync());

        // NOTE: whether the exported Excel/CSV/PDF file's contents actually include the User
        // Account column can't practically be asserted here — Playwright would need to download
        // and parse a binary spreadsheet/PDF, which this suite's conventions avoid faking. This
        // test is limited to confirming the export entrypoint is present/enabled while the column
        // itself is part of the grid it operates on.
    }

    /// <summary>
    /// Creates a fresh, uniquely-named Acme employee with no linked user account, to use as the
    /// invite target for QuickInvite_ForNoUserEmployee_OpensPreselectedDialog_AndCompletesToPendingInvitation.
    ///
    /// That test used to invite the shared seeded "Sophie Laurent" (NoUserEmployeeName) directly,
    /// which is also read (as an expected "No User" row) by
    /// UserAccountColumn_ShowsNoUserIconLabelAndInviteLink_ForEmployeeWithoutAccount in this same
    /// class, and was invited by UserAdministrationManagementTests' Resend/Cancel toolbar test too
    /// — under real parallel execution those tests race to invite/resend/cancel her, leaving her
    /// status unpredictable depending on run order. A freshly created employee has no linked user
    /// account (employee creation does not provision one), so it satisfies the same "eligible
    /// NoUser invite target" precondition without mutating shared seed data. The read-only tests
    /// above still use Sophie Laurent directly since they never mutate her.
    /// </summary>
    private async Task<string> CreateFreshUninvitedEmployeeAsync()
    {
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        var unique    = Guid.NewGuid().ToString("N")[..8];
        var lastName  = $"Invite{unique}";
        var workEmail = $"e2e.invite{unique}@acme.example";

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
        await empEdit.SelectDropdownAsync("Position Profile", "QA Engineer");
        await empEdit.SaveNewEmployeeAsync();

        return lastName;
    }

    [Fact]
    public async Task QuickInvite_ForNoUserEmployee_OpensPreselectedDialog_AndCompletesToPendingInvitation()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var list = new EmployeeListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        var targetName = await CreateFreshUninvitedEmployeeAsync();

        await list.GoToAsync(AcmeId);

        Assert.True(await list.HasInviteUserLinkAsync(targetName),
            $"Expected '{targetName}' (freshly created with no linked user account) to show the 'Invite User' link");

        await list.ClickInviteUserLinkAsync(targetName);

        // "Employee" is always applied automatically (fixed badge, not a selectable role) — no
        // additional roles are needed for this happy-path invite.
        await list.CompleteQuickInviteAsync([]);

        // Completing the invite calls EmployeeList.HandleInviteUserDialogCompleted, which reloads
        // via LoadAsync() rather than navigating — the URL should stay on the employee list.
        Assert.Contains("/employees", _page.Url);
        Assert.DoesNotContain("/user-administration", _page.Url);

        var successMessage = await list.GetActionSuccessMessageAsync();
        Assert.Equal("Invitation sent.", successMessage);

        var text = await list.GetUserAccountStatusTextAsync(targetName);
        Assert.Contains("Invited", text);

        Assert.False(await list.HasInviteUserLinkAsync(targetName),
            "The 'Invite' action should no longer be offered once the employee has a pending invitation");
    }
}
