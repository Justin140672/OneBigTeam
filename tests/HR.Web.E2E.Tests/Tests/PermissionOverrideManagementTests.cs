using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the "Permission Override" (IAM-04) feature added to UserAdministrationList.razor /
/// UserDetail.razor / AddRoleOverrideDialog.razor:
/// - The User Administration grid's "Permission Override" column badge is absent for a user with
///   no active overrides and present once one exists.
/// - UserDetail.razor's "Permission Overrides" card renders both its empty state and its
///   populated state (with the header badge alongside Account/Invitation Status).
/// - Adding an override end-to-end via "+ Add Override" (role, Grant/Deny, reason, optional
///   expiry) — the "creating a record" case for this feature.
/// - Removing an override via the per-row "Remove" button, including the badge clearing once the
///   last override is removed — the "delete" case.
/// - Submitting the Add Override dialog without a reason surfaces the client-side validation
///   error rather than silently failing.
///
/// Uses Laura Bennett (HR Administrator) as the acting persona against the seeded Acme company,
/// matching UserAdministrationManagementTests. Each test targets a distinct seeded persona
/// (James Okafor / Priya Shah / Sarah Chen) that isn't mutated by UserAdministrationManagementTests
/// or any other test in this class, so override state doesn't leak across tests or collide with
/// unrelated suites sharing this long-lived E2E database.
/// </summary>
public sealed class PermissionOverrideManagementTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string HrAdminEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task PermissionOverrideBadge_AbsentWithNoOverride_PresentAfterAdding()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var list  = new UserAdministrationListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        await list.GoToAsync(AcmeId);

        // James Okafor is a seeded active account (Employee, Manager) not otherwise touched by
        // this test class or UserAdministrationManagementTests, so it starts with no overrides.
        Assert.False(await list.HasPermissionOverrideBadgeAsync("James Okafor"),
            "Expected no 'Permission Override' badge for a user with no active overrides");

        await list.OpenUserDetailAsync("James Okafor");

        var detail = new UserDetailPage(_page, _fixture.WebBaseUrl);
        var access = new UserAccessDetailPage(_page, _fixture.WebBaseUrl);

        await detail.OpenAccessDetailsAsync();
        Assert.True(await access.HasNoOverridesMessageAsync(),
            "Expected the empty-state message when a user has no permission overrides");

        await access.OpenAddOverrideDialogAsync();
        var dialog = new AddRoleOverrideDialog(_page);

        await dialog.SelectRoleAsync("Recruiter");
        await dialog.SelectOverrideTypeAsync("Grant");
        await dialog.FillReasonAsync("Temporary cover for recruiter on leave");
        await dialog.SaveAsync();

        Assert.Equal("Permission override added.", await access.GetSuccessMessageAsync());

        Assert.True(await access.HasOverrideAsync("Recruiter"),
            "Expected the newly added Recruiter override to appear in the Permission Overrides card");
        Assert.Equal("Grant", await access.GetOverrideTypeAsync("Recruiter"));

        await list.GoToAsync(AcmeId);
        Assert.True(await list.HasPermissionOverrideBadgeAsync("James Okafor"),
            "Expected the grid's 'Permission Override' badge to show after adding an override");
    }

    [Fact]
    public async Task AddOverrideWithExpiry_ThenRemove_ClearsOverrideAndBadge()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var list  = new UserAdministrationListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        await list.GoToAsync(AcmeId);
        // Priya Shah (Employee, Company Administrator) — untouched by other tests in this file.
        await list.OpenUserDetailAsync("Priya Shah");

        var detail = new UserDetailPage(_page, _fixture.WebBaseUrl);
        var access = new UserAccessDetailPage(_page, _fixture.WebBaseUrl);
        await detail.OpenAccessDetailsAsync();
        await access.OpenAddOverrideDialogAsync();
        var dialog = new AddRoleOverrideDialog(_page);

        await dialog.SelectRoleAsync("Manager");
        await dialog.SelectOverrideTypeAsync("Deny");
        await dialog.FillReasonAsync("Restrict manager access pending investigation");
        await dialog.FillExpiresAsync("31/12/2026");
        await dialog.SaveAsync();

        Assert.Equal("Permission override added.", await access.GetSuccessMessageAsync());
        Assert.True(await access.HasOverrideAsync("Manager"));
        Assert.Equal("Deny", await access.GetOverrideTypeAsync("Manager"));

        // ── Remove: the "delete" action for this feature ──
        await access.RemoveOverrideAsync("Manager");

        Assert.Equal("Permission override removed.", await access.GetSuccessMessageAsync());
        Assert.True(await access.HasNoOverridesMessageAsync(),
            "Expected the empty-state message after removing the only override");

        await list.GoToAsync(AcmeId);
        Assert.False(await list.HasPermissionOverrideBadgeAsync("Priya Shah"),
            "Expected the grid badge to clear once the user's last override is removed");
    }

    [Fact]
    public async Task AddOverride_WithoutReason_ShowsValidationError()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var list  = new UserAdministrationListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        await list.GoToAsync(AcmeId);
        // Sarah Chen (Employee, Company Administrator, Manager) — untouched elsewhere in this file.
        await list.OpenUserDetailAsync("Sarah Chen");

        var detail = new UserDetailPage(_page, _fixture.WebBaseUrl);
        var access = new UserAccessDetailPage(_page, _fixture.WebBaseUrl);
        await detail.OpenAccessDetailsAsync();
        await access.OpenAddOverrideDialogAsync();
        var dialog = new AddRoleOverrideDialog(_page);

        await dialog.SelectRoleAsync("Recruiter");
        await dialog.SelectOverrideTypeAsync("Grant");
        // Reason intentionally left blank.
        await dialog.SaveAsync();

        Assert.True(await dialog.IsVisibleAsync(),
            "Expected the Add Override dialog to stay open when the reason is missing");
        Assert.Equal("A reason is required.", await dialog.GetErrorAsync());

        Assert.False(await access.HasOverrideAsync("Recruiter"),
            "Expected no override to have been created when validation failed");
    }
}
