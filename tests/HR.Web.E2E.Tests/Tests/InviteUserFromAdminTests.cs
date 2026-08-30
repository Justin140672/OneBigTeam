using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// ADM-01: the User Administration list page's own "Invite User" toolbar action, which opens the
/// 4-step <c>InviteUserWizard.razor</c> (Employee → Email → Roles → Review → Send). Complements
/// <see cref="UserAdministrationManagementTests"/>, which still covers the Employee-List row-level
/// Quick Invite path plus resend/cancel/disable/enable/manage-roles.
///
/// Uses Laura Bennett (HR Administrator) against the seeded Acme company. "Emma Jones" is a seeded
/// Acme employee with no dev-persona user account (same target as
/// UserAdministrationManagementTests.InviteEmployee_EndToEnd_ShowsPendingInvitationInGrid) — if the
/// seed data changes so she gains an account, she'll drop out of the wizard's invitable list and
/// this test fails fast; pick another unlinked seeded employee at that point.
/// </summary>
public sealed class InviteUserFromAdminTests(HrAdminPersonaFixture fixture)
    : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private const string HrAdminEmail = "laura.bennett@acme.example";
    private const string UninvitedEmployeeName = "Emma Jones";

    [Fact]
    public async Task InviteUserWizard_FromAdminToolbar_CreatesPendingInvitation()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var list = new UserAdministrationListPage(_page, _fixture.WebBaseUrl);
        var wizard = new InviteUserWizardPage(_page);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        await list.GoToAsync(AcmeId);

        await wizard.OpenFromToolbarAsync();
        await wizard.SelectEmployeeAsync(UninvitedEmployeeName);
        await wizard.ConfirmEmailAsync();
        await wizard.SelectRolesAsync("Manager");

        var review = await wizard.GetReviewTextAsync();
        Assert.Contains(UninvitedEmployeeName, review);

        await wizard.SendAsync();

        Assert.True(await list.HasRowAsync(UninvitedEmployeeName),
            $"Expected '{UninvitedEmployeeName}' to appear in the grid after the wizard invite");
        Assert.Equal("Pending", await list.GetInvitationStatusAsync(UninvitedEmployeeName));
    }
}
