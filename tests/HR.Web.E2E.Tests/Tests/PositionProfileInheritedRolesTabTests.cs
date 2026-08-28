using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies the "Inherited Roles" tab on the Position Profile edit page
/// (PositionProfileInheritedRolesTab.razor), backed by
/// GET/PUT /api/companies/{companyId}/positions/{positionProfileId}/role-defaults.
///
/// Uses the seeded "QA Engineer" position profile (Acme Corporation) rather than "Software
/// Engineer" (mutated by PositionProfileRequiredDocumentsTabTests/PositionProfileNoticePeriodOverrideTests/
/// PositionProfileManagementTests) so this file doesn't need to coordinate with those on a shared
/// entity. "QA Engineer" has no inherited roles configured in seed data, and nothing else in the
/// suite edits its role defaults (other files only ever select it in a Position Profile dropdown).
///
/// Inherits <see cref="PositionRoleDefaultsSerialTestBase"/> because every test here saves the
/// full role-defaults list for "QA Engineer" — see that base's remarks for why this must be
/// serialized against EffectiveAccessViewTests, which mutates a different seeded position profile
/// via the same read-modify-write-the-whole-list endpoint.
/// </summary>
public sealed class PositionProfileInheritedRolesTabTests(HrAdminPersonaFixture fixture) : PositionRoleDefaultsSerialTestBase(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";
    private const string QaEngineerTitle = "QA Engineer";

    [Fact]
    public async Task InheritedRolesTab_IsVisible_When_EditingExistingProfile()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var list   = new PositionProfileListPage(_page, _fixture.WebBaseUrl);
        var ppEdit = new PositionProfileEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await list.GoToAsync(AcmeId);
        await list.OpenPositionProfileAsync(QaEngineerTitle);

        Assert.True(
            await ppEdit.HasInheritedRolesTabAsync(),
            "Expected an 'Inherited Roles' tab on the position profile edit page");
    }

    [Fact]
    public async Task InheritedRolesTab_IsNotVisible_When_CreatingNewProfile()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var ppEdit = new PositionProfileEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await ppEdit.GoToNewAsync(AcmeId);

        Assert.False(
            await ppEdit.HasInheritedRolesTabAsync(),
            "Expected no 'Inherited Roles' tab when creating a new position profile");
    }

    [Fact]
    public async Task InheritedRolesTab_CanCheckRole_AndPersistsAfterReload()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var list   = new PositionProfileListPage(_page, _fixture.WebBaseUrl);
        var ppEdit = new PositionProfileEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await list.GoToAsync(AcmeId);
        await list.OpenPositionProfileAsync(QaEngineerTitle);
        await ppEdit.OpenInheritedRolesTabAsync();

        Assert.False(
            await ppEdit.IsInheritedRoleCheckedAsync("Recruiter"),
            "Test setup assumption violated: 'Recruiter' was already an inherited role for QA Engineer");

        try
        {
            // ── Check + Save ─────────────────────────────────────────────────
            await ppEdit.SetInheritedRoleCheckedAsync("Recruiter", true);
            await ppEdit.SaveInheritedRolesAsync();

            Assert.True(await ppEdit.HasInheritedRolesSuccessAlertAsync(),
                "Expected a success alert after saving inherited roles");
            Assert.Contains("Recruiter", await ppEdit.GetCheckedInheritedRoleNamesAsync());

            // Reload the page (fresh navigation) and reopen the tab to confirm persistence.
            await list.GoToAsync(AcmeId);
            await list.OpenPositionProfileAsync(QaEngineerTitle);
            await ppEdit.OpenInheritedRolesTabAsync();

            Assert.True(
                await ppEdit.IsInheritedRoleCheckedAsync("Recruiter"),
                "Expected 'Recruiter' to remain checked after reloading the page");
        }
        finally
        {
            // ── Cleanup: leave QA Engineer's inherited roles as we found them ──
            await ppEdit.SetInheritedRoleCheckedAsync("Recruiter", false);
            await ppEdit.SaveInheritedRolesAsync();
        }
    }

    [Fact]
    public async Task InheritedRolesTab_CanUncheckPreviouslyCheckedRole_AndPersistsRemoval()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var list   = new PositionProfileListPage(_page, _fixture.WebBaseUrl);
        var ppEdit = new PositionProfileEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await list.GoToAsync(AcmeId);
        await list.OpenPositionProfileAsync(QaEngineerTitle);
        await ppEdit.OpenInheritedRolesTabAsync();

        // ── Arrange: check "Manager" first so there's something to remove ──────
        await ppEdit.SetInheritedRoleCheckedAsync("Manager", true);
        await ppEdit.SaveInheritedRolesAsync();
        Assert.Contains("Manager", await ppEdit.GetCheckedInheritedRoleNamesAsync());

        // ── Act: uncheck + save ─────────────────────────────────────────────
        await ppEdit.SetInheritedRoleCheckedAsync("Manager", false);
        await ppEdit.SaveInheritedRolesAsync();

        Assert.True(await ppEdit.HasInheritedRolesSuccessAlertAsync(),
            "Expected a success alert after saving inherited roles");
        Assert.DoesNotContain("Manager", await ppEdit.GetCheckedInheritedRoleNamesAsync());

        // Reload and confirm the removal persisted server-side, not just client state.
        await list.GoToAsync(AcmeId);
        await list.OpenPositionProfileAsync(QaEngineerTitle);
        await ppEdit.OpenInheritedRolesTabAsync();

        Assert.False(
            await ppEdit.IsInheritedRoleCheckedAsync("Manager"),
            "Expected 'Manager' to remain unchecked after reloading the page");
    }
}
