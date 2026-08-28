using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.GetAccessReview;
using HR.Modules.Identity.Tests.Infrastructure;

namespace HR.Modules.Identity.Tests;

/// <summary>
/// IAM-08: integration tests for <see cref="GetAccessReviewHandler"/>.
/// Verifies that the access-review report correctly lists privileged users and
/// the source of each privilege (direct, position-inherited, override).
/// </summary>
[Collection("IdentityDatabase")]
public class GetAccessReviewHandlerTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly FakeClock Clock = new(Now.UtcDateTime);

    private GetAccessReviewHandler BuildHandler(IReadOnlyList<Guid> employeeIds) =>
        new(fixture.BuildContext(), new FakeEmployeeNameReader(), new FakeEmployeeAudienceReader(employeeIds), Clock);

    [Fact]
    public async Task HandleAsync_Returns_Empty_When_No_Employees()
    {
        var handler = BuildHandler([]);
        var result = await handler.HandleAsync(
            new GetAccessReviewRequest { CompanyId = Guid.NewGuid() },
            CancellationToken.None);
        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Employees_With_Only_Baseline_Employee_Role()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(employeeId, "baseline@test.com", "h", "Base", "User", Now));
            db.UserRoles.Add(UserRole.Create(employeeId, SystemRoles.Employee, Now));
            await db.SaveChangesAsync();
        }

        var handler = BuildHandler([employeeId]);
        var result  = await handler.HandleAsync(
            new GetAccessReviewRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task HandleAsync_Lists_User_With_Direct_Privileged_Role()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var roleId     = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(employeeId, "direct@test.com", "h", "Direct", "User", Now));
            db.Roles.Add(Role.Create(roleId, "HrManager", Now));
            db.UserRoles.Add(UserRole.Create(employeeId, roleId, Now));
            await db.SaveChangesAsync();
        }

        var handler = BuildHandler([employeeId]);
        var result  = await handler.HandleAsync(
            new GetAccessReviewRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        var item = Assert.Single(result.Items);
        Assert.Equal(employeeId, item.EmployeeId);
        Assert.Single(item.Privileges, p => p.Source == "Direct" && p.RoleId == roleId);
    }

    [Fact]
    public async Task HandleAsync_Lists_User_With_Inherited_Role_Via_Position()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var roleId     = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(employeeId, "pos@test.com", "h", "Pos", "User", Now));
            db.Roles.Add(Role.Create(roleId, "TeamLead", Now));
            db.Positions.Add(Position.Create(positionId, companyId, "Engineering Lead", Now));
            db.PositionRoles.Add(PositionRole.Create(positionId, roleId, Now));
            db.UserPositions.Add(UserPosition.Create(employeeId, positionId, Now));
            await db.SaveChangesAsync();
        }

        var handler = BuildHandler([employeeId]);
        var result  = await handler.HandleAsync(
            new GetAccessReviewRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        var privilege = Assert.Single(result.Items[0].Privileges, p => p.RoleId == roleId);
        Assert.StartsWith("Position:", privilege.Source);
        Assert.Contains("Engineering Lead", privilege.Source);
    }

    [Fact]
    public async Task HandleAsync_Lists_User_With_Active_Grant_Override()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var roleId     = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(employeeId, "override@test.com", "h", "Over", "User", Now));
            db.Roles.Add(Role.Create(roleId, "Admin", Now));
            db.EmployeeRoleOverrides.Add(
                EmployeeRoleOverride.Create(companyId, employeeId, roleId, EmployeeRoleOverrideType.Grant, "audited reason", null, Now));
            await db.SaveChangesAsync();
        }

        var handler = BuildHandler([employeeId]);
        var result  = await handler.HandleAsync(
            new GetAccessReviewRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Items[0].Privileges, p => p.Source == "Override");
    }

    [Fact]
    public async Task HandleAsync_Marks_Override_As_Expiring_Soon_When_Within_14_Days()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var roleId     = Guid.NewGuid();
        var soonExpiry = Now.AddDays(7);

        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(employeeId, "expiring@test.com", "h", "Exp", "User", Now));
            db.Roles.Add(Role.Create(roleId, "TempAdmin", Now));
            db.EmployeeRoleOverrides.Add(
                EmployeeRoleOverride.Create(companyId, employeeId, roleId, EmployeeRoleOverrideType.Grant, "temp", soonExpiry, Now));
            await db.SaveChangesAsync();
        }

        var handler = BuildHandler([employeeId]);
        var result  = await handler.HandleAsync(
            new GetAccessReviewRequest { CompanyId = companyId },
            CancellationToken.None);

        var privilege = Assert.Single(result.Items[0].Privileges);
        Assert.True(privilege.IsExpiringSoon);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Include_Expired_Grant_Overrides()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var roleId     = Guid.NewGuid();
        var pastExpiry = Now.AddDays(-1); // already expired

        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(employeeId, "expired@test.com", "h", "Exp", "User", Now));
            db.Roles.Add(Role.Create(roleId, "ExpiredRole", Now));
            db.EmployeeRoleOverrides.Add(
                EmployeeRoleOverride.Create(companyId, employeeId, roleId, EmployeeRoleOverrideType.Grant, "was temporary", pastExpiry, Now));
            await db.SaveChangesAsync();
        }

        var handler = BuildHandler([employeeId]);
        var result  = await handler.HandleAsync(
            new GetAccessReviewRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task HandleAsync_Is_Company_Scoped_Via_Audience_Reader()
    {
        var companyA  = Guid.NewGuid();
        var employeeA = Guid.NewGuid();
        var employeeB = Guid.NewGuid();
        var roleId    = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(employeeA, "a@test.com", "h", "A", "User", Now));
            db.Users.Add(ApplicationUser.Create(employeeB, "b@test.com", "h", "B", "User", Now));
            db.Roles.Add(Role.Create(roleId, "Manager", Now));
            db.UserRoles.Add(UserRole.Create(employeeA, roleId, Now));
            db.UserRoles.Add(UserRole.Create(employeeB, roleId, Now));
            await db.SaveChangesAsync();
        }

        // Only employee A is visible for company A.
        var handler = BuildHandler([employeeA]);
        var result  = await handler.HandleAsync(
            new GetAccessReviewRequest { CompanyId = companyA },
            CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(employeeA, result.Items[0].EmployeeId);
    }
}
