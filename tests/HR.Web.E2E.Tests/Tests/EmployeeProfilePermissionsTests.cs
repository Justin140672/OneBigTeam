using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Permission-based visibility for the redesigned employee profile page: a user without
/// Session.CanManageEmployees must not be able to reach "Edit details" or the "Users &amp; Access"
/// administration controls for another employee's profile. EmployeeEdit.razor's own LoadAsync
/// gates the *entire* page (both view and edit routes) behind Session.CanManageEmployees,
/// redirecting to Session.MyProfileUrl otherwise (see its `if (!Session.CanManageEmployees)`
/// guard) — so the "Edit details" button / "Users &amp; Access" card are never even reachable to
/// probe for, and the correct end-to-end assertion is that direct navigation to either route is
/// redirected away entirely. Same persona and redirect-assertion pattern as
/// CompanyAdministratorAccessTests.CompanyAdministrator_CannotAccess_EmployeeList, extended here to
/// the specific employee profile view/edit routes introduced by this redesign.
/// </summary>
public sealed class EmployeeProfilePermissionsTests(PriyaShahPersonaFixture fixture) : RoleE2ETestBase<PriyaShahPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    // Marcus Diallo — seeded HR Advisor (also used by EmployeeEditCloseBehaviorTests/AssignManagerTests).
    private static readonly Guid MarcusId = Guid.Parse("30000000-0000-0000-0000-000000000006");

    // Priya has the CompanyAdministrator role only — CanManageEmployees is false, CanManageCompany
    // is true (see CompanyAdministratorAccessTests).
    private const string CompanyAdminEmail = "priya.shah@acme.example";

    [Fact]
    public async Task CompanyAdministratorOnly_CannotReach_EmployeeProfile_ViewRoute()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/employees/{MarcusId}/view");
        await WaitForUrlToStopContainingAsync("/employees");

        var finalUrl = _page.Url;
        Assert.False(
            finalUrl.Contains($"/employees/{MarcusId}/view", StringComparison.OrdinalIgnoreCase),
            $"Expected Priya (CompanyAdministrator-only, no CanManageEmployees) to be redirected away from " +
            $"another employee's profile view route, but ended up at: {finalUrl}");

        // Never even reaches the DOM with an "Edit details" button / "Users & Access" card to
        // probe for — the whole page is gated server-side, not merely a hidden button.
        Assert.False(await _page.Locator("[data-testid='edit-details-button']").IsVisibleAsync(),
            "Expected no 'Edit details' button to ever render for a user without CanManageEmployees");
    }

    [Fact]
    public async Task CompanyAdministratorOnly_CannotReach_EmployeeProfile_EditRoute()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(CompanyAdminEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/companies/{AcmeId}/employees/{MarcusId}");
        await WaitForUrlToStopContainingAsync($"/employees/{MarcusId}");

        var finalUrl = _page.Url;
        Assert.False(
            finalUrl.TrimEnd('/').EndsWith($"/employees/{MarcusId}", StringComparison.OrdinalIgnoreCase),
            $"Expected Priya (CompanyAdministrator-only, no CanManageEmployees) to be redirected away from " +
            $"another employee's profile edit route, but ended up at: {finalUrl}");

        Assert.False(await _page.Locator(".employee-edit-sticky-bar").IsVisibleAsync(),
            "Expected no editable Users & Access / sticky action bar to ever render for a user without CanManageEmployees");
    }
}
