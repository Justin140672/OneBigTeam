using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.ListEmployeeRoleOverrides;
using HR.Modules.Identity.Tests.Infrastructure;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class ListEmployeeRoleOverridesHandlerTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);

    private ListEmployeeRoleOverridesHandler BuildHandler(FakeTargetUserCompanyGuard? guard = null) =>
        new(fixture.BuildContext(), guard ?? new FakeTargetUserCompanyGuard());

    private async Task<Guid> SeedUser(string suffix)
    {
        await using var db = fixture.BuildContext();
        var userId = Guid.NewGuid();
        db.Users.Add(ApplicationUser.Create(userId, $"user-{suffix}-{userId:N}@test.com", "hash", "Test", "User", Now));
        await db.SaveChangesAsync();
        return userId;
    }

    private async Task SeedOverride(Guid companyId, Guid userId, Guid roleId, EmployeeRoleOverrideType type, DateTimeOffset assignedAt)
    {
        await using var db = fixture.BuildContext();
        db.EmployeeRoleOverrides.Add(EmployeeRoleOverride.Create(companyId, userId, roleId, type, "Reason", null, assignedAt));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Target_User_Not_A_Company_Member()
    {
        var targetUserId = await SeedUser("wrong-company");
        var handler = BuildHandler(new FakeTargetUserCompanyGuard(isMember: false));

        var result = await handler.HandleAsync(
            new ListEmployeeRoleOverridesRequest { CompanyId = Guid.NewGuid(), UserId = targetUserId },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_List_When_No_Overrides_Exist()
    {
        var targetUserId = await SeedUser("no-overrides");
        var handler = BuildHandler();

        var result = await handler.HandleAsync(
            new ListEmployeeRoleOverridesRequest { CompanyId = Guid.NewGuid(), UserId = targetUserId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Overrides);
    }

    [Fact]
    public async Task HandleAsync_Returns_Only_Overrides_For_The_Requested_User_And_Company_Newest_First()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var targetUserId = await SeedUser("target");
        var otherUserId = await SeedUser("other-user");

        await SeedOverride(companyId, targetUserId, SystemRoles.Manager, EmployeeRoleOverrideType.Grant, Now);
        await SeedOverride(companyId, targetUserId, SystemRoles.Recruiter, EmployeeRoleOverrideType.Deny, Now.AddMinutes(5));
        await SeedOverride(companyId, otherUserId, SystemRoles.Manager, EmployeeRoleOverrideType.Grant, Now); // different user
        await SeedOverride(otherCompanyId, targetUserId, SystemRoles.Employee, EmployeeRoleOverrideType.Grant, Now); // different company

        var handler = BuildHandler();

        var result = await handler.HandleAsync(
            new ListEmployeeRoleOverridesRequest { CompanyId = companyId, UserId = targetUserId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Overrides.Count);
        // Newest-first: the Recruiter deny (assigned later) must come before the Manager grant.
        Assert.Equal(SystemRoles.Recruiter, result.Value.Overrides[0].RoleId);
        Assert.Equal(SystemRoles.Manager, result.Value.Overrides[1].RoleId);
    }
}
