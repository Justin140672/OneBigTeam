using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.GetAccessReview;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class GetAccessReviewHandlerTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly FakeClock Clock = new(Now.UtcDateTime);

    private GetAccessReviewHandler BuildHandler(IReadOnlyList<Guid> employeeIds) =>
        new(fixture.BuildContext(), new FakeEmployeeNameReader(), new FakeEmployeeAudienceReader(employeeIds), Clock);

    [Fact]
    public async Task HandleAsync_Is_Scoped_To_The_Employee_Ids_Returned_By_The_Audience_Reader()
    {
        // The audience reader (not a CompanyId filter on the roles themselves) is what determines
        // which users are in-scope for the review — a user with a privileged role who isn't
        // returned by GetAllEmployeeIdsAsync for this company must not appear.
        var companyId = Guid.NewGuid();
        var inScopeUser = Guid.NewGuid();
        var outOfScopeUser = Guid.NewGuid();
        var roleId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(inScopeUser, "in-scope@test.com", "hash", "In", "Scope", Now));
            db.Users.Add(ApplicationUser.Create(outOfScopeUser, "out-of-scope@test.com", "hash", "Out", "OfScope", Now));
            db.Roles.Add(Role.Create(roleId, $"ScopedRole.{Guid.NewGuid():N}", Now));
            db.UserRoles.Add(UserRole.Create(inScopeUser, roleId, Now));
            db.UserRoles.Add(UserRole.Create(outOfScopeUser, roleId, Now));
            await db.SaveChangesAsync();
        }

        // Only inScopeUser is returned by the audience reader for this company.
        var handler = BuildHandler([inScopeUser]);

        var result = await handler.HandleAsync(
            new GetAccessReviewRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(inScopeUser, result.Items[0].EmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_When_Company_Has_No_Employees()
    {
        var handler = BuildHandler([]);

        var result = await handler.HandleAsync(
            new GetAccessReviewRequest { CompanyId = Guid.NewGuid() }, CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Users_With_Only_The_Baseline_Employee_Role()
    {
        var companyId = Guid.NewGuid();
        var baselineOnlyUser = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(baselineOnlyUser, "baseline@test.com", "hash", "Baseline", "Only", Now));
            db.UserRoles.Add(UserRole.Create(baselineOnlyUser, SystemRoles.Employee, Now));
            await db.SaveChangesAsync();
        }

        var handler = BuildHandler([baselineOnlyUser]);

        var result = await handler.HandleAsync(
            new GetAccessReviewRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task HandleAsync_Includes_A_User_With_A_Direct_Non_Baseline_Role_With_Direct_Source()
    {
        var companyId = Guid.NewGuid();
        var privilegedUser = Guid.NewGuid();
        var roleId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(privilegedUser, "direct-priv@test.com", "hash", "Direct", "Priv", Now));
            db.Roles.Add(Role.Create(roleId, $"AccessReviewDirectRole.{Guid.NewGuid():N}", Now));
            db.UserRoles.Add(UserRole.Create(privilegedUser, roleId, Now));
            await db.SaveChangesAsync();
        }

        var handler = BuildHandler([privilegedUser]);

        var result = await handler.HandleAsync(
            new GetAccessReviewRequest { CompanyId = companyId }, CancellationToken.None);

        var item = Assert.Single(result.Items, i => i.EmployeeId == privilegedUser);
        var privilege = Assert.Single(item.Privileges, p => p.RoleId == roleId);
        Assert.Equal("Direct", privilege.Source);
    }

    [Fact]
    public async Task HandleAsync_Includes_A_User_With_A_Position_Inherited_Non_Baseline_Role_With_Position_Source()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var roleId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(employeeId, "inherited-priv@test.com", "hash", "Inherited", "Priv", Now));
            db.Positions.Add(Position.Create(positionId, companyId, "Access Review Position", Now));
            db.Roles.Add(Role.Create(roleId, $"AccessReviewInheritedRole.{Guid.NewGuid():N}", Now));
            db.PositionRoles.Add(PositionRole.Create(positionId, roleId, Now));
            db.UserPositions.Add(UserPosition.Create(employeeId, positionId, Now));
            await db.SaveChangesAsync();
        }

        var handler = BuildHandler([employeeId]);

        var result = await handler.HandleAsync(
            new GetAccessReviewRequest { CompanyId = companyId }, CancellationToken.None);

        var item = Assert.Single(result.Items, i => i.EmployeeId == employeeId);
        var privilege = Assert.Single(item.Privileges, p => p.RoleId == roleId);
        Assert.Equal("Position:Access Review Position", privilege.Source);
    }

    [Fact]
    public async Task HandleAsync_Includes_A_User_With_An_Active_Grant_Override_With_Override_Source_And_Expiry_Info()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var expiresAt = Now.AddDays(7);

        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(employeeId, "grant-priv@test.com", "hash", "Grant", "Priv", Now));
            db.Roles.Add(Role.Create(roleId, $"AccessReviewGrantRole.{Guid.NewGuid():N}", Now));
            db.EmployeeRoleOverrides.Add(
                EmployeeRoleOverride.Create(companyId, employeeId, roleId, EmployeeRoleOverrideType.Grant, "Cover", expiresAt, Now));
            await db.SaveChangesAsync();
        }

        var handler = BuildHandler([employeeId]);

        var result = await handler.HandleAsync(
            new GetAccessReviewRequest { CompanyId = companyId }, CancellationToken.None);

        var item = Assert.Single(result.Items, i => i.EmployeeId == employeeId);
        var privilege = Assert.Single(item.Privileges, p => p.RoleId == roleId);
        Assert.Equal("Override", privilege.Source);
        Assert.Equal(expiresAt, privilege.OverrideExpiresAt);
        Assert.True(privilege.IsExpiringSoon);
    }

    [Fact]
    public async Task HandleAsync_Excludes_A_User_Whose_Only_Grant_Override_Has_Expired()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var roleId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(employeeId, "expired-priv@test.com", "hash", "Expired", "Priv", Now));
            db.Roles.Add(Role.Create(roleId, $"AccessReviewExpiredGrantRole.{Guid.NewGuid():N}", Now));
            db.EmployeeRoleOverrides.Add(
                EmployeeRoleOverride.Create(companyId, employeeId, roleId, EmployeeRoleOverrideType.Grant, "Expired", Now.AddSeconds(-1), Now));
            await db.SaveChangesAsync();
        }

        try
        {
            var handler = BuildHandler([employeeId]);

            var result = await handler.HandleAsync(
                new GetAccessReviewRequest { CompanyId = companyId }, CancellationToken.None);

            Assert.Empty(result.Items);
        }
        finally
        {
            // This is deliberately the only override seeded anywhere in this test class with an
            // ExpiresAt in the past — ExpireEmployeeRoleOverridesJobTests (same shared
            // "IdentityDatabase" collection) sweeps the *entire* override table with no company
            // filter, so a genuinely-expired row left behind here would otherwise be picked up by
            // that job's "nothing expired" assertion. Clean up explicitly rather than relying on
            // test execution order.
            await using var cleanup = fixture.BuildContext();
            var stray = await cleanup.EmployeeRoleOverrides.FirstOrDefaultAsync(o => o.UserId == employeeId && o.RoleId == roleId);
            if (stray is not null)
            {
                cleanup.EmployeeRoleOverrides.Remove(stray);
                await cleanup.SaveChangesAsync();
            }
        }
    }

    [Fact]
    public async Task HandleAsync_Excludes_A_User_Whose_Only_Override_Is_A_Deny_Override()
    {
        // Deny overrides don't confer privilege — only active Grant overrides do.
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var roleId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(employeeId, "deny-only@test.com", "hash", "Deny", "Only", Now));
            db.Roles.Add(Role.Create(roleId, $"AccessReviewDenyOnlyRole.{Guid.NewGuid():N}", Now));
            db.EmployeeRoleOverrides.Add(
                EmployeeRoleOverride.Create(companyId, employeeId, roleId, EmployeeRoleOverrideType.Deny, "No privilege", null, Now));
            await db.SaveChangesAsync();
        }

        var handler = BuildHandler([employeeId]);

        var result = await handler.HandleAsync(
            new GetAccessReviewRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.Empty(result.Items);
    }
}
