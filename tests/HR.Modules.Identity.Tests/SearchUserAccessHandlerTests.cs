using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.SearchUserAccess;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Tests;

/// <summary>
/// IAM-08: integration tests for <see cref="SearchUserAccessHandler"/>.
/// Verifies filtering by role, position/inherited role, override state and expiry.
/// </summary>
[Collection("IdentityDatabase")]
public class SearchUserAccessHandlerTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly FakeClock Clock = new(Now.UtcDateTime);

    private SearchUserAccessHandler BuildHandler(IReadOnlyList<Guid> employeeIds) =>
        new(fixture.BuildContext(), new FakeEmployeeNameReader(), new FakeEmployeeAudienceReader(employeeIds), Clock);

    // -----------------------------------------------------------------------
    // 1. Empty company
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HandleAsync_Returns_Empty_When_No_Employees()
    {
        var handler = BuildHandler([]);
        var result = await handler.HandleAsync(
            new SearchUserAccessRequest { CompanyId = Guid.NewGuid(), Page = 1, PageSize = 25 },
            CancellationToken.None);
        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Items);
    }

    // -----------------------------------------------------------------------
    // 2. Filter by direct role
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HandleAsync_Filters_By_Direct_RoleId()
    {
        var companyId  = Guid.NewGuid();
        var employee1  = Guid.NewGuid();
        var employee2  = Guid.NewGuid();
        var roleA      = Guid.NewGuid();
        var roleB      = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(employee1, "e1@test.com", "h", "Emp", "One", Now));
            db.Users.Add(ApplicationUser.Create(employee2, "e2@test.com", "h", "Emp", "Two", Now));
            db.Roles.Add(Role.Create(roleA, "RoleA", Now));
            db.Roles.Add(Role.Create(roleB, "RoleB", Now));
            db.UserRoles.Add(UserRole.Create(employee1, roleA, Now));
            db.UserRoles.Add(UserRole.Create(employee2, roleB, Now));
            await db.SaveChangesAsync();
        }

        var handler = BuildHandler([employee1, employee2]);
        var result  = await handler.HandleAsync(
            new SearchUserAccessRequest { CompanyId = companyId, RoleId = roleA, Page = 1, PageSize = 25 },
            CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(employee1, result.Items[0].EmployeeId);
    }

    // -----------------------------------------------------------------------
    // 3. Filter by inherited (position) role
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HandleAsync_Filters_By_Inherited_Role_Via_Position()
    {
        var companyId  = Guid.NewGuid();
        var employee1  = Guid.NewGuid();
        var employee2  = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var roleA      = Guid.NewGuid();
        var roleB      = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(employee1, "pos1@test.com", "h", "Pos", "One", Now));
            db.Users.Add(ApplicationUser.Create(employee2, "pos2@test.com", "h", "Pos", "Two", Now));
            db.Roles.Add(Role.Create(roleA, "PositionRole", Now));
            db.Roles.Add(Role.Create(roleB, "OtherRole", Now));
            db.Positions.Add(Position.Create(positionId, companyId, "Team Lead", Now));
            db.PositionRoles.Add(PositionRole.Create(positionId, roleA, Now));
            db.UserPositions.Add(UserPosition.Create(employee1, positionId, Now));
            db.UserRoles.Add(UserRole.Create(employee2, roleB, Now));
            await db.SaveChangesAsync();
        }

        var handler = BuildHandler([employee1, employee2]);
        var result  = await handler.HandleAsync(
            new SearchUserAccessRequest { CompanyId = companyId, RoleId = roleA, Page = 1, PageSize = 25 },
            CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(employee1, result.Items[0].EmployeeId);
        Assert.Single(result.Items[0].InheritedRoles, r => r.RoleId == roleA);
    }

    // -----------------------------------------------------------------------
    // 4. Filter by override state — HasGrantOverride
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HandleAsync_Filters_By_HasGrantOverride()
    {
        var companyId  = Guid.NewGuid();
        var employee1  = Guid.NewGuid();
        var employee2  = Guid.NewGuid();
        var roleId     = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(employee1, "over1@test.com", "h", "Over", "One", Now));
            db.Users.Add(ApplicationUser.Create(employee2, "over2@test.com", "h", "Over", "Two", Now));
            db.Roles.Add(Role.Create(roleId, "TestRole", Now));
            db.EmployeeRoleOverrides.Add(EmployeeRoleOverride.Create(companyId, employee1, roleId, EmployeeRoleOverrideType.Grant, "test", null, Now));
            await db.SaveChangesAsync();
        }

        var handler = BuildHandler([employee1, employee2]);
        var result  = await handler.HandleAsync(
            new SearchUserAccessRequest { CompanyId = companyId, OverrideState = OverrideStateFilter.HasGrantOverride, Page = 1, PageSize = 25 },
            CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(employee1, result.Items[0].EmployeeId);
    }

    // -----------------------------------------------------------------------
    // 5. Temporary overrides approaching expiry are identifiable
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HandleAsync_Marks_Overrides_Expiring_Within_14_Days()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var roleId     = Guid.NewGuid();
        var soonExpiry = Now.AddDays(7);   // within 14-day window

        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(employeeId, "expiry@test.com", "h", "Exp", "User", Now));
            db.Roles.Add(Role.Create(roleId, "TempRole", Now));
            db.EmployeeRoleOverrides.Add(EmployeeRoleOverride.Create(companyId, employeeId, roleId, EmployeeRoleOverrideType.Grant, "temporary", soonExpiry, Now));
            await db.SaveChangesAsync();
        }

        var handler = BuildHandler([employeeId]);
        var result  = await handler.HandleAsync(
            new SearchUserAccessRequest { CompanyId = companyId, OverrideState = OverrideStateFilter.HasExpiringOverride, Page = 1, PageSize = 25 },
            CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        var over = Assert.Single(result.Items[0].Overrides);
        Assert.True(over.IsExpiringSoon);
    }

    // -----------------------------------------------------------------------
    // 6. Company scoping — users from another company are not returned
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HandleAsync_Is_Scoped_To_Company_Via_Audience_Reader()
    {
        // The audience reader controls which employee IDs are visible for the company.
        // An employee from a different company simply won't be in the list.
        var companyA  = Guid.NewGuid();
        var employeeA = Guid.NewGuid();
        var employeeB = Guid.NewGuid(); // belongs to company B — not in reader list
        var roleId    = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(employeeA, "a@test.com", "h", "A", "User", Now));
            db.Users.Add(ApplicationUser.Create(employeeB, "b@test.com", "h", "B", "User", Now));
            db.Roles.Add(Role.Create(roleId, "SomeRole", Now));
            db.UserRoles.Add(UserRole.Create(employeeA, roleId, Now));
            db.UserRoles.Add(UserRole.Create(employeeB, roleId, Now));
            await db.SaveChangesAsync();
        }

        // Only company A's employee is returned by the audience reader.
        var handler = BuildHandler([employeeA]);
        var result  = await handler.HandleAsync(
            new SearchUserAccessRequest { CompanyId = companyA, RoleId = roleId, Page = 1, PageSize = 25 },
            CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(employeeA, result.Items[0].EmployeeId);
    }
}
