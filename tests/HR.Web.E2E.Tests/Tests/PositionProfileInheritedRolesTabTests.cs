using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

public sealed class PositionProfileInheritedRolesTabTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
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
    public async Task InheritedRolesTab_EmployeeRole_IsAlwaysCheckedAndDisabled()
    {
        var login  = new LoginPage(_page, _fixture.WebBaseUrl);
        var list   = new PositionProfileListPage(_page, _fixture.WebBaseUrl);
        var ppEdit = new PositionProfileEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await list.GoToAsync(AcmeId);
        await list.OpenPositionProfileAsync(QaEngineerTitle);
        await ppEdit.OpenInheritedRolesTabAsync();

        Assert.True(await ppEdit.IsInheritedRoleCheckedAsync("Employee"),
            "Expected the 'Employee' inherited role to always be checked");
        Assert.True(await ppEdit.IsInheritedRoleDisabledAsync("Employee"),
            "Expected the 'Employee' inherited role checkbox to be disabled (it cannot be unchecked)");
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
            await ppEdit.SetInheritedRoleCheckedAsync("Recruiter", true);
            await ppEdit.SaveInheritedRolesAsync();

            Assert.True(await ppEdit.HasInheritedRolesSuccessAlertAsync(),
                "Expected a success alert after saving inherited roles");
            Assert.Contains("Recruiter", await ppEdit.GetCheckedInheritedRoleNamesAsync());

            await list.GoToAsync(AcmeId);
            await list.OpenPositionProfileAsync(QaEngineerTitle);
            await ppEdit.OpenInheritedRolesTabAsync();

            Assert.True(
                await ppEdit.IsInheritedRoleCheckedAsync("Recruiter"),
                "Expected 'Recruiter' to remain checked after reloading the page");
        }
        finally
        {
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

        await ppEdit.SetInheritedRoleCheckedAsync("Manager", true);
        await ppEdit.SaveInheritedRolesAsync();
        Assert.Contains("Manager", await ppEdit.GetCheckedInheritedRoleNamesAsync());

        await ppEdit.SetInheritedRoleCheckedAsync("Manager", false);
        await ppEdit.SaveInheritedRolesAsync();

        Assert.True(await ppEdit.HasInheritedRolesSuccessAlertAsync(),
            "Expected a success alert after saving inherited roles");
        Assert.DoesNotContain("Manager", await ppEdit.GetCheckedInheritedRoleNamesAsync());

        await list.GoToAsync(AcmeId);
        await list.OpenPositionProfileAsync(QaEngineerTitle);
        await ppEdit.OpenInheritedRolesTabAsync();

        Assert.False(
            await ppEdit.IsInheritedRoleCheckedAsync("Manager"),
            "Expected 'Manager' to remain unchecked after reloading the page");
    }
}
