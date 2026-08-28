using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the read-only "Effective Access" (IAM-05) card added to UserDetail.razor, below the
/// "Permission Overrides" panel (IAM-04): position profile summary, Direct/Inherited/Overrides
/// lists, Effective Roles with source badges (e.g. "Direct", "Override"), Effective Permissions
/// with a Scope badge, and the Denied Permissions list.
///
/// This is a read-only view backed by GET /api/companies/{companyId}/users/{employeeId}/effective-access,
/// so there is no create/edit/delete flow to cover — these tests focus on: does the section render
/// its full structure for a normal user, and does adding a permission override (the same flow
/// PermissionOverrideManagementTests already exercises) get reflected both in the Overrides list
/// and in the Effective Roles' source badges.
///
/// Uses Laura Bennett (HR Administrator) as the acting persona against the seeded Acme company,
/// matching PermissionOverrideManagementTests. Each test targets a distinct seeded persona not
/// mutated by PermissionOverrideManagementTests or any other test in this class, so override state
/// doesn't leak across tests or collide with unrelated suites sharing this long-lived E2E database.
///
/// Inherits <see cref="PositionRoleDefaultsSerialTestBase"/> (rather than plain
/// RoleE2ETestBase&lt;HrAdminPersonaFixture&gt;) because
/// <see cref="EffectiveAccess_ReflectsPositionInheritedRole_And_DenyOverride_RemovesIt"/> below
/// mutates the seeded "QA Engineer" position profile's inherited-role defaults via the same
/// read-modify-write-the-whole-list endpoint that PositionProfileInheritedRolesTabTests exercises —
/// see that base's remarks.
/// </summary>
public sealed class EffectiveAccessViewTests(HrAdminPersonaFixture fixture) : PositionRoleDefaultsSerialTestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string HrAdminEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task EffectiveAccess_RendersFullStructure_ForUserWithDirectRoleAndNoOverrides()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var list  = new UserAdministrationListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        await list.GoToAsync(AcmeId);
        // Tom Williams (Developer) — a seeded active account not otherwise touched by
        // PermissionOverrideManagementTests or any other test in this class.
        await list.OpenUserDetailAsync("Tom Williams");

        var detail = new UserDetailPage(_page, _fixture.WebBaseUrl);
        await detail.WaitForEffectiveAccessLoadedAsync();

        Assert.False(await detail.HasEffectiveAccessErrorAsync(),
            "Expected the Effective Access card to load without an error for a normal user");

        var directRoleNames = await detail.GetDirectRoleNamesAsync();
        var accountRoleNames = await detail.GetRoleNamesAsync();
        Assert.NotEmpty(accountRoleNames);
        Assert.Equal(accountRoleNames.OrderBy(n => n), directRoleNames.OrderBy(n => n));

        // No permission overrides exist for this user, so the Overrides list should be empty and
        // every direct role should be reflected in Effective Roles with a "Direct" source badge.
        Assert.Empty(await detail.GetEffectiveAccessOverrideRoleNamesAsync());

        foreach (var roleName in accountRoleNames)
        {
            Assert.True(await detail.HasEffectiveRoleAsync(roleName),
                $"Expected '{roleName}' to appear in Effective Roles");
            var sources = await detail.GetEffectiveRoleSourcesAsync(roleName);
            Assert.Contains("Direct", sources);
        }

        // Position profile is either "No position assigned." or a named position — either is a
        // valid rendered state, but the section itself must always render some text.
        var positionText = await detail.GetPositionProfileTextAsync();
        Assert.False(string.IsNullOrWhiteSpace(positionText));
    }

    [Fact]
    public async Task EffectiveAccess_ReflectsGrantOverride_InOverridesListAndEffectiveRoles()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var list  = new UserAdministrationListPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        await list.GoToAsync(AcmeId);
        // Carlos Rivera (Account Executive) — a seeded active account not otherwise touched by
        // PermissionOverrideManagementTests or any other test in this class.
        await list.OpenUserDetailAsync("Carlos Rivera");

        var detail = new UserDetailPage(_page, _fixture.WebBaseUrl);
        await detail.WaitForEffectiveAccessLoadedAsync();

        // Sanity check: the Recruiter role isn't already an effective role for this user before
        // the override is added, so the assertions below actually attribute the change to the
        // override rather than to pre-existing seeded state.
        Assert.False(await detail.HasEffectiveRoleAsync("Recruiter"),
            "Test setup assumption violated: Recruiter was already an effective role before adding the override");

        await detail.OpenAddOverrideDialogAsync();
        var dialog = new AddRoleOverrideDialog(_page);

        await dialog.SelectRoleAsync("Recruiter");
        await dialog.SelectOverrideTypeAsync("Grant");
        await dialog.FillReasonAsync("Temporary recruiter access for backfill coverage");
        await dialog.SaveAsync();

        Assert.Equal("Permission override added.", await detail.GetSuccessMessageAsync());

        await detail.WaitForEffectiveAccessLoadedAsync();

        Assert.Contains("Recruiter", await detail.GetEffectiveAccessOverrideRoleNamesAsync());

        Assert.True(await detail.HasEffectiveRoleAsync("Recruiter"),
            "Expected the Grant-overridden Recruiter role to appear in Effective Roles");
        var sources = await detail.GetEffectiveRoleSourcesAsync("Recruiter");
        Assert.Contains("Override", sources);
    }

    /// <summary>
    /// Exercises the full inherited-roles + denied-permission path added by the Inherited Roles
    /// tab (PositionProfileInheritedRolesTab.razor): granting a role as a position default shows
    /// up as an Inherited Role and an Effective Role sourced "Position:{PositionName}" for every
    /// employee holding that position, and denying that same role via a permission override both
    /// removes it from Effective Roles and surfaces its otherwise-inherited permissions under
    /// Denied Permissions.
    ///
    /// Uses a freshly created employee (not a shared seeded persona) assigned to the seeded
    /// "QA Engineer" position profile — safe to assign employees to per CreateEmployeeTests'
    /// remarks (unlike "Senior Software Engineer", which several parallel tests rely on remaining
    /// unassigned/selectable). "Recruiter" grants employee.read/employee.create/document.read
    /// (RolePermissionConfiguration); document.read is also granted by the base Employee role, so
    /// it's excluded from Denied Permissions by design (still effective via another role) —
    /// employee.read/employee.create are not, so those are the two permissions asserted below.
    /// </summary>
    [Fact]
    public async Task EffectiveAccess_ReflectsPositionInheritedRole_And_DenyOverride_RemovesIt()
    {
        const string QaEngineerTitle = "QA Engineer";

        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
        var ppList  = new PositionProfileListPage(_page, _fixture.WebBaseUrl);
        var ppEdit  = new PositionProfileEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        // ── Create a fresh employee assigned to "QA Engineer" ───────────────────────
        var unique    = Guid.NewGuid().ToString("N")[..8];
        var lastName  = $"EffAccessE2E{unique}";
        var workEmail = $"e2e.effaccess{unique}@acme.example";

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
        await empEdit.SelectDropdownAsync("Position Profile", QaEngineerTitle);
        await empEdit.SaveNewEmployeeAsync();

        await empList.ClickEmployeeAsync(lastName);
        var employeeId = Guid.Parse(System.Text.RegularExpressions.Regex.Match(
            _page.Url, @"/employees/([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})").Groups[1].Value);

        var detail = new UserDetailPage(_page, _fixture.WebBaseUrl);

        try
        {
            // ── Grant "Recruiter" as an inherited role on "QA Engineer" ─────────────
            await ppList.GoToAsync(AcmeId);
            await ppList.OpenPositionProfileAsync(QaEngineerTitle);
            await ppEdit.OpenInheritedRolesTabAsync();

            Assert.False(
                await ppEdit.IsInheritedRoleCheckedAsync("Recruiter"),
                "Test setup assumption violated: 'Recruiter' was already an inherited role for QA Engineer");

            await ppEdit.SetInheritedRoleCheckedAsync("Recruiter", true);
            await ppEdit.SaveInheritedRolesAsync();

            // ── Confirm it's reflected as an Inherited Role / Effective Role ────────
            await detail.GoToAsync(AcmeId, employeeId);
            await detail.WaitForEffectiveAccessLoadedAsync();

            Assert.Contains("Recruiter", await detail.GetInheritedRoleNamesAsync());
            Assert.True(await detail.HasEffectiveRoleAsync("Recruiter"),
                "Expected the position-inherited Recruiter role to appear in Effective Roles");
            Assert.Contains($"Position:{QaEngineerTitle}", await detail.GetEffectiveRoleSourcesAsync("Recruiter"));

            // No overrides yet, so nothing should be denied.
            Assert.Equal(0, await detail.GetDeniedPermissionsCountAsync());

            // ── Deny the Recruiter role via a permission override ───────────────────
            await detail.OpenAddOverrideDialogAsync();
            var dialog = new AddRoleOverrideDialog(_page);

            await dialog.SelectRoleAsync("Recruiter");
            await dialog.SelectOverrideTypeAsync("Deny");
            await dialog.FillReasonAsync("Testing denied-permission attribution for a position-inherited role");
            await dialog.SaveAsync();

            Assert.Equal("Permission override added.", await detail.GetSuccessMessageAsync());
            await detail.WaitForEffectiveAccessLoadedAsync();

            // ── The role is no longer effectively granted, and its otherwise-inherited ──
            // permissions (except document.read, still granted via the base Employee role)
            // now show up as denied.
            Assert.False(await detail.HasEffectiveRoleAsync("Recruiter"),
                "Expected the Deny override to remove the position-inherited Recruiter role from Effective Roles");

            Assert.Equal(2, await detail.GetDeniedPermissionsCountAsync());
            Assert.True(await detail.HasDeniedPermissionAsync("employee.read"));
            Assert.True(await detail.HasDeniedPermissionAsync("employee.create"));
            Assert.False(await detail.HasDeniedPermissionAsync("document.read"),
                "document.read is also granted via the base Employee role, so it should not appear as denied");
        }
        finally
        {
            // ── Cleanup: remove the override and reset QA Engineer's inherited roles ───
            // so neither leaks into other tests sharing this long-lived E2E database.
            if (await detail.HasOverrideAsync("Recruiter"))
                await detail.RemoveOverrideAsync("Recruiter");

            await ppList.GoToAsync(AcmeId);
            await ppList.OpenPositionProfileAsync(QaEngineerTitle);
            await ppEdit.OpenInheritedRolesTabAsync();
            if (await ppEdit.IsInheritedRoleCheckedAsync("Recruiter"))
            {
                await ppEdit.SetInheritedRoleCheckedAsync("Recruiter", false);
                await ppEdit.SaveInheritedRolesAsync();
            }
        }
    }
}
