using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Tests;

/// <summary>
/// Verifies the actual seeded <see cref="RolePermission"/> rows applied by the real
/// EF Core migrations (including the CompanyAdministrator permission narrowing
/// migration) match the expected permission set for each role.
/// </summary>
[Collection("IdentityDatabase")]
public class RolePermissionSeedTests(IdentityDatabaseFixture fixture)
{
    [Fact]
    public async Task CompanyAdministrator_Has_Exactly_The_Expected_Permission_Set()
    {
        await using var db = fixture.BuildContext();

        var actual = await db.RolePermissions
            .Where(rp => rp.RoleId == SystemRoles.CompanyAdministrator)
            .Select(rp => rp.PermissionId)
            .ToListAsync();

        // IAM-06: role.assign removed from this set — see RolePermissionConfiguration.cs remarks.
        // No authorization policy actually grants Company Administrator role-assignment access
        // (Features/UpdateUserRoles is gated by "users:manage", which is HR Administrator-only), so
        // that grant was misleading seeded data implying a capability the role could never exercise.
        var expected = new HashSet<Guid>
        {
            SystemPermissions.CompanyRead,
            SystemPermissions.CompanyEdit,
            SystemPermissions.OnboardingView,
            SystemPermissions.OnboardingManage,
            SystemPermissions.SubscriptionManage,
            SystemPermissions.SupportManage,
        };

        Assert.Equal(expected, actual.ToHashSet());
    }

    [Theory]
    [InlineData(nameof(SystemPermissions.CompanyRead))]
    [InlineData(nameof(SystemPermissions.CompanyEdit))]
    public async Task CompanyAdministrator_Retains_Company_Profile_Permissions(string permissionName)
    {
        await using var db = fixture.BuildContext();

        var permissionId = permissionName switch
        {
            nameof(SystemPermissions.CompanyRead) => SystemPermissions.CompanyRead,
            nameof(SystemPermissions.CompanyEdit) => SystemPermissions.CompanyEdit,
            _ => throw new ArgumentOutOfRangeException(nameof(permissionName))
        };

        var exists = await db.RolePermissions.AnyAsync(rp =>
            rp.RoleId == SystemRoles.CompanyAdministrator && rp.PermissionId == permissionId);

        Assert.True(exists, $"Expected CompanyAdministrator to retain {permissionName}.");
    }

    [Theory]
    [InlineData(nameof(SystemPermissions.SelfRead))]
    [InlineData(nameof(SystemPermissions.SelfEdit))]
    [InlineData(nameof(SystemPermissions.EmployeeRead))]
    [InlineData(nameof(SystemPermissions.EmployeeEdit))]
    [InlineData(nameof(SystemPermissions.EmployeeCreate))]
    [InlineData(nameof(SystemPermissions.EmployeeDelete))]
    [InlineData(nameof(SystemPermissions.LeaveRequest))]
    [InlineData(nameof(SystemPermissions.LeaveApprove))]
    [InlineData(nameof(SystemPermissions.DocumentManage))]
    [InlineData(nameof(SystemPermissions.SicknessRead))]
    [InlineData(nameof(SystemPermissions.SicknessManage))]
    [InlineData(nameof(SystemPermissions.RoleAssign))]
    public async Task CompanyAdministrator_No_Longer_Has_HR_Admin_Permissions(string permissionName)
    {
        await using var db = fixture.BuildContext();

        var permissionId = permissionName switch
        {
            nameof(SystemPermissions.SelfRead) => SystemPermissions.SelfRead,
            nameof(SystemPermissions.SelfEdit) => SystemPermissions.SelfEdit,
            nameof(SystemPermissions.EmployeeRead) => SystemPermissions.EmployeeRead,
            nameof(SystemPermissions.EmployeeEdit) => SystemPermissions.EmployeeEdit,
            nameof(SystemPermissions.EmployeeCreate) => SystemPermissions.EmployeeCreate,
            nameof(SystemPermissions.EmployeeDelete) => SystemPermissions.EmployeeDelete,
            nameof(SystemPermissions.LeaveRequest) => SystemPermissions.LeaveRequest,
            nameof(SystemPermissions.LeaveApprove) => SystemPermissions.LeaveApprove,
            nameof(SystemPermissions.DocumentManage) => SystemPermissions.DocumentManage,
            nameof(SystemPermissions.SicknessRead) => SystemPermissions.SicknessRead,
            nameof(SystemPermissions.SicknessManage) => SystemPermissions.SicknessManage,
            nameof(SystemPermissions.RoleAssign) => SystemPermissions.RoleAssign,
            _ => throw new ArgumentOutOfRangeException(nameof(permissionName))
        };

        var exists = await db.RolePermissions.AnyAsync(rp =>
            rp.RoleId == SystemRoles.CompanyAdministrator && rp.PermissionId == permissionId);

        Assert.False(exists, $"Expected CompanyAdministrator to no longer have {permissionName}.");
    }
}
