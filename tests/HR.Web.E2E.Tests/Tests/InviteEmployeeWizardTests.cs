using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the "Invite Employee" wizard's (InviteUserDialog.razor) UI changes:
/// - The dialog header reads "Invite Employee", not the older "Invite Employee User".
/// - When launched with a pre-selected employee (EmployeeList's row-level "Invite User" quick
///   action), there is no separate "Employee" step shown at all — see EmployeeUserAccountColumnTests
///   for that entry point's own coverage (InviteDialogHasEmployeePickerAsync /
///   InviteDialogHasEmployeeStepPillAsync).
/// - The Roles step (step 2) shows "Employee" as a fixed, non-removable badge rather than a
///   selectable multiselect item, alongside a separate optional "additional roles" multiselect.
/// - The Confirm step (step 3) does not display a separate "Email" row.
///
/// Uses this entry point (UserAdministrationListPage's "+ Invite Employee" button) since it's the
/// one flow that shows the full step nav (Employee, Roles, Confirm), unlike the pre-selected Quick
/// Invite flow covered elsewhere. Deliberately cancels out of the wizard on step 3 rather than
/// completing it, so the seeded "Emma Jones" employee stays uninvited for
/// UserAdministrationManagementTests.InviteEmployee_EndToEnd_ShowsPendingInvitationInGrid, which
/// depends on her still being available to invite.
/// </summary>
[Collection("E2E")]
public sealed class InviteEmployeeWizardTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string HrAdminEmail = "laura.bennett@acme.example";
    private const string UninvitedEmployeeName = "Emma Jones";

    [Fact]
    public async Task InviteDialog_HeaderReads_InviteEmployee_NotInviteEmployeeUser()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var list  = new UserAdministrationListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        await list.GoToAsync(AcmeId);
        await list.OpenInviteDialogAsync();

        Assert.True(await list.InviteDialog.IsVisibleAsync(),
            "Expected a dialog titled exactly 'Invite Employee' to open");
        Assert.False(
            await _page.GetByRole(AriaRole.Dialog, new() { Name = "Invite Employee User" }).IsVisibleAsync(),
            "Did not expect the older 'Invite Employee User' dialog title");
    }

    [Fact]
    public async Task InviteDialog_RolesStep_ShowsEmployeeAsFixedBadge_NotEmailRow_OnConfirmStep()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var list  = new UserAdministrationListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        await list.GoToAsync(AcmeId);
        await list.OpenInviteDialogAsync();

        // Step 1: Employee — the full (non-pre-selected) entry point does show this step.
        Assert.True(await list.HasEmployeeStepPillAsync(),
            "Expected the 'Employee' step pill for the non-pre-selected entry point");

        await DropDownSelector.SelectAsync(_page, list.InviteDialog, UninvitedEmployeeName);
        await list.InviteDialog.GetByRole(AriaRole.Button, new() { Name = "Next" }).ClickAsync();

        // Step 2: Roles — "Employee" is a fixed badge, not a removable multiselect item.
        Assert.True(await list.IsEmployeeRoleBadgeVisibleAsync(),
            "Expected 'Employee' to be shown as a fixed badge on the Roles step");
        Assert.False(
            await list.InviteDialog.Locator("input[placeholder='Select one or more roles']").IsVisibleAsync(),
            "Did not expect the older 'Select one or more roles' multiselect placeholder (Employee is no longer selectable there)");
        Assert.True(
            await list.InviteDialog.Locator("input[placeholder='Select additional roles (optional)']").IsVisibleAsync(),
            "Expected a separate optional 'additional roles' multiselect alongside the fixed Employee badge");

        await list.InviteDialog.GetByRole(AriaRole.Button, new() { Name = "Next" }).ClickAsync();

        // Step 3: Confirm — no separate Email row.
        Assert.False(await list.HasConfirmEmailRowAsync(),
            "Did not expect a separate 'Email' row on the Confirm step");

        // Cancel rather than submit, so Emma Jones remains uninvited for the sibling
        // UserAdministrationManagementTests happy-path test.
        await list.InviteDialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
    }
}
