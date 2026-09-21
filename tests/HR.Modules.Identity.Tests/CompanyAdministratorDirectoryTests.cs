using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Services;
using HR.Modules.Identity.Tests.Infrastructure;

namespace HR.Modules.Identity.Tests;

/// <summary>
/// Exercises the real IdentityDbContext-backed CompanyAdministratorDirectory — the Identity-owned
/// implementation of ICompanyAdministratorDirectory consumed cross-module by Customer Release
/// Notifications' ProductUpdateRecipientResolver. Mirrors IHrAdministratorDirectory's shape; see
/// HrAdministratorDirectory for the sibling implementation this deliberately parallels (scoped to
/// SystemRoles.CompanyAdministrator instead of SystemRoles.HrAdministrator, and additionally
/// filtered to UserProfile.IsActive == true).
/// </summary>
[Collection("IdentityDatabase")]
public class CompanyAdministratorDirectoryTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 10, 0, 0, TimeSpan.Zero);

    private async Task<UserProfile> SeedUserAsync(Guid companyId, Guid roleId, bool isActive = true)
    {
        await using var db = fixture.BuildContext();
        var profile = UserProfile.Create(
            Guid.NewGuid(), Guid.NewGuid(), companyId, $"user-{Guid.NewGuid():N}@example.com", "Test", "User", Now);
        db.UserProfiles.Add(profile);
        db.UserRoles.Add(UserRole.Create(profile.Id, roleId, Now));
        await db.SaveChangesAsync();

        if (!isActive)
        {
            await using var deactivateDb = fixture.BuildContext();
            var tracked = await deactivateDb.UserProfiles.FindAsync(profile.Id);
            tracked!.Deactivate(Now);
            await deactivateDb.SaveChangesAsync();
        }

        return profile;
    }

    [Fact]
    public async Task GetActiveCompanyAdministratorEmployeeIdsAsync_Returns_Active_CompanyAdministrator()
    {
        var companyId = Guid.NewGuid();
        var admin = await SeedUserAsync(companyId, SystemRoles.CompanyAdministrator);

        var directory = new CompanyAdministratorDirectory(fixture.BuildContext());

        var result = await directory.GetActiveCompanyAdministratorEmployeeIdsAsync(companyId, CancellationToken.None);

        Assert.Contains(admin.Id, result);
    }

    [Fact]
    public async Task GetActiveCompanyAdministratorEmployeeIdsAsync_Excludes_Inactive_CompanyAdministrator()
    {
        var companyId = Guid.NewGuid();
        var inactiveAdmin = await SeedUserAsync(companyId, SystemRoles.CompanyAdministrator, isActive: false);

        var directory = new CompanyAdministratorDirectory(fixture.BuildContext());

        var result = await directory.GetActiveCompanyAdministratorEmployeeIdsAsync(companyId, CancellationToken.None);

        Assert.DoesNotContain(inactiveAdmin.Id, result);
    }

    [Theory]
    [InlineData("Employee")]
    [InlineData("Manager")]
    [InlineData("HrAdministrator")]
    public async Task GetActiveCompanyAdministratorEmployeeIdsAsync_Excludes_NonCompanyAdministrator_Roles(string roleName)
    {
        var companyId = Guid.NewGuid();
        var roleId = roleName switch
        {
            "Employee" => SystemRoles.Employee,
            "Manager" => SystemRoles.Manager,
            "HrAdministrator" => SystemRoles.HrAdministrator,
            _ => throw new ArgumentOutOfRangeException(nameof(roleName)),
        };
        var user = await SeedUserAsync(companyId, roleId);

        var directory = new CompanyAdministratorDirectory(fixture.BuildContext());

        var result = await directory.GetActiveCompanyAdministratorEmployeeIdsAsync(companyId, CancellationToken.None);

        Assert.DoesNotContain(user.Id, result);
    }

    [Fact]
    public async Task GetActiveCompanyAdministratorEmployeeIdsAsync_Is_Scoped_To_The_Given_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var adminA = await SeedUserAsync(companyA, SystemRoles.CompanyAdministrator);
        var adminB = await SeedUserAsync(companyB, SystemRoles.CompanyAdministrator);

        var directory = new CompanyAdministratorDirectory(fixture.BuildContext());

        var result = await directory.GetActiveCompanyAdministratorEmployeeIdsAsync(companyA, CancellationToken.None);

        Assert.Contains(adminA.Id, result);
        Assert.DoesNotContain(adminB.Id, result);
    }

    [Fact]
    public async Task GetActiveCompanyAdministratorEmployeeIdsAsync_Returns_Empty_For_Company_With_No_Administrators()
    {
        var companyId = Guid.NewGuid();

        var directory = new CompanyAdministratorDirectory(fixture.BuildContext());

        var result = await directory.GetActiveCompanyAdministratorEmployeeIdsAsync(companyId, CancellationToken.None);

        Assert.Empty(result);
    }
}
