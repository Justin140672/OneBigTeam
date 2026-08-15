using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the "User Account" status column and row-level "Invite User" Quick Invite action added
/// to the Employee List (tickets #90/#91) — closing the E2E coverage gap tracked by ticket #96.
///
/// Personas/employees used (all seeded Acme data, same convention as
/// UserAdministrationManagementTests):
/// - "Laura Bennett" — HR Administrator, seeded active dev-persona account -> UserAccountStatus
///   "Active".
/// - "Carlos Rivera" — seeded active dev-persona account, used (and restored) as the "Disabled"
///   arrangement so as not to permanently disable a persona another test might rely on.
/// - "Sophie Laurent" — seeded Acme employee with no corresponding dev-persona user account (see
///   EmployeesModule's MakeAcme seed list vs. IdentityModule.SeedDevUserAsync's persona list) ->
///   UserAccountStatus "NoUser". Deliberately distinct from "Emma Jones" (used by
///   UserAdministrationManagementTests.InviteEmployee_EndToEnd_ShowsPendingInvitationInGrid) so the
///   two suites' invite side effects can't collide. As with Emma Jones there, if the seed data
///   ever changes so Sophie Laurent gains an account, QuickInvite_ForNoUserEmployee... below will
///   fail fast (no "Invite User" link to click) rather than silently passing against the wrong row.
///
/// "InvitationExpired" is not covered here: reaching that state requires a genuinely expired
/// invitation (time-based), which can't be arranged through the UI within a single test run
/// without directly manipulating the database — doing so would violate this suite's "don't fake
/// what can't genuinely be verified" convention. The icon/label mapping for it lives in
/// EmployeeList.UserAccountStatusDisplay alongside the other four covered here.
/// </summary>
[Collection("E2E")]
public sealed class EmployeeUserAccountColumnTests(AppFixture fixture) : E2ETestBase(fixture)
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
        Assert.Contains("No User", text);

        var iconClass = await list.GetUserAccountStatusIconClassAsync(NoUserEmployeeName);
        Assert.Contains("fa-circle-minus", iconClass);

        Assert.True(await list.HasInviteUserLinkAsync(NoUserEmployeeName),
            "Expected the 'Invite User' link on a row with no linked user account");
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
            "The 'Invite User' link should only render for 'No User' rows, not Active ones");
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
                "The 'Invite User' link should not render for a Disabled row");
        }
        finally
        {
            await userAdminList.GoToAsync(AcmeId);
            await userAdminList.OpenUserDetailAsync(DisableTargetEmployeeName);
            if (await detail.GetAccountStatusAsync() == "Disabled")
                await detail.EnableAccountAsync();
        }
    }

    // Alphabetical rank of each rendered "User Account" label, matching the underlying
    // EmployeeUserAccountStatus enum's string sort order ("Active" < "Disabled" <
    // "InvitationExpired" < "NoUser" < "PendingInvitation") — used to prove ascending/descending
    // sort without needing to know exactly which employees/statuses exist on the shared,
    // long-lived E2E database at any given time.
    private static readonly Dictionary<string, int> StatusRank = new()
    {
        ["Active"] = 0,
        ["Disabled"] = 1,
        ["Invitation Expired"] = 2,
        ["No User"] = 3,
        ["Pending Invitation"] = 4,
    };

    [Fact]
    public async Task UserAccountColumn_IsSortable_ClickingHeaderSortsRowsByStatus()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var list = new EmployeeListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);
        await list.GoToAsync(AcmeId);

        await list.ClickUserAccountHeaderAsync(expectedDirectionClass: "e-ascending");
        var ascendingRanks = (await list.GetVisibleUserAccountStatusesInOrderAsync())
            .Select(l => StatusRank.TryGetValue(l, out var r) ? r : -1)
            .Where(r => r >= 0)
            .ToList();
        Assert.NotEmpty(ascendingRanks);
        Assert.Equal(ascendingRanks.OrderBy(r => r).ToList(), ascendingRanks);

        await list.ClickUserAccountHeaderAsync(expectedDirectionClass: "e-descending");
        var descendingRanks = (await list.GetVisibleUserAccountStatusesInOrderAsync())
            .Select(l => StatusRank.TryGetValue(l, out var r) ? r : -1)
            .Where(r => r >= 0)
            .ToList();
        Assert.NotEmpty(descendingRanks);
        Assert.Equal(descendingRanks.OrderByDescending(r => r).ToList(), descendingRanks);
    }

    [Fact]
    public async Task UserAccountColumn_ExcelFilter_NarrowsGridToMatchingStatus()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var list = new EmployeeListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);
        await list.GoToAsync(AcmeId);

        // Sanity: the unfiltered grid has a mix of statuses, including at least one "No User" row.
        var initialLabels = await list.GetVisibleUserAccountStatusesInOrderAsync();
        Assert.Contains("No User", initialLabels);

        try
        {
            await list.OpenUserAccountColumnFilterAsync();
            await list.ApplyUserAccountColumnFilterAsync("No User");

            var filteredLabels = await list.GetVisibleUserAccountStatusesInOrderAsync();
            Assert.NotEmpty(filteredLabels);
            Assert.All(filteredLabels, label => Assert.Equal("No User", label));
        }
        finally
        {
            await list.ClearUserAccountColumnFilterAsync();
        }
    }

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

        var exportButton = _page.GetByRole(AriaRole.Button, new() { Name = "Export" });
        Assert.True(await exportButton.IsVisibleAsync());
        Assert.False(await exportButton.IsDisabledAsync());

        // NOTE: whether the exported Excel/CSV/PDF file's contents actually include the User
        // Account column can't practically be asserted here — Playwright would need to download
        // and parse a binary spreadsheet/PDF, which this suite's conventions avoid faking. This
        // test is limited to confirming the export entrypoint is present/enabled while the column
        // itself is part of the grid it operates on.
    }

    [Fact]
    public async Task QuickInvite_ForNoUserEmployee_OpensPreselectedDialog_AndCompletesToPendingInvitation()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var list = new EmployeeListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);
        await list.GoToAsync(AcmeId);

        Assert.True(await list.HasInviteUserLinkAsync(NoUserEmployeeName),
            $"Expected '{NoUserEmployeeName}' (seeded with no linked user account) to show the 'Invite User' link");

        await list.ClickInviteUserLinkAsync(NoUserEmployeeName);

        // "Employee" is always applied automatically (fixed badge, not a selectable role) — no
        // additional roles are needed for this happy-path invite.
        await list.CompleteQuickInviteAsync([]);

        // Completing the invite calls EmployeeList.HandleInviteUserDialogCompleted, which reloads
        // via LoadAsync() rather than navigating — the URL should stay on the employee list.
        Assert.Contains("/employees", _page.Url);
        Assert.DoesNotContain("/user-administration", _page.Url);

        var successMessage = await list.GetActionSuccessMessageAsync();
        Assert.Equal("Invitation sent.", successMessage);

        var text = await list.GetUserAccountStatusTextAsync(NoUserEmployeeName);
        Assert.Contains("Pending Invitation", text);

        Assert.False(await list.HasInviteUserLinkAsync(NoUserEmployeeName),
            "The 'Invite User' link should no longer render once the employee has a pending invitation");
    }
}
