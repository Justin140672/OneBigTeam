using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.SearchUserAccess;
using HR.Modules.Identity.Tests.Infrastructure;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class SearchUserAccessHandlerTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly FakeClock Clock = new(Now.UtcDateTime);

    private SearchUserAccessHandler BuildHandler(IReadOnlyList<Guid> employeeIds) =>
        new(fixture.BuildContext(), new FakeEmployeeNameReader(), new FakeEmployeeAudienceReader(employeeIds), Clock);

    [Fact]
    public async Task HandleAsync_Is_Scoped_To_The_Employee_Ids_Returned_By_The_Audience_Reader()
    {
        // The audience reader (not a CompanyId filter on the roles themselves) determines which
        // users are in-scope for the search — a user not returned by GetAllEmployeeIdsAsync for
        // this company must not appear, even if their role/user rows technically exist.
        var companyId = Guid.NewGuid();
        var inScopeUser = Guid.NewGuid();
        var outOfScopeUser = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(inScopeUser, "in-scope-search@test.com", "hash", "In", "Scope", Now));
            db.Users.Add(ApplicationUser.Create(outOfScopeUser, "out-of-scope-search@test.com", "hash", "Out", "OfScope", Now));
            await db.SaveChangesAsync();
        }

        var handler = BuildHandler([inScopeUser]);

        var result = await handler.HandleAsync(
            new SearchUserAccessRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(inScopeUser, result.Items[0].EmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_When_Company_Has_No_Employees()
    {
        var handler = BuildHandler([]);

        var result = await handler.HandleAsync(
            new SearchUserAccessRequest { CompanyId = Guid.NewGuid() }, CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_RoleId_Across_Direct_Inherited_And_Override_Sources()
    {
        var companyId = Guid.NewGuid();
        var directUser = Guid.NewGuid();
        var inheritedUser = Guid.NewGuid();
        var overrideUser = Guid.NewGuid();
        var unrelatedUser = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var otherRoleId = Guid.NewGuid();
        var positionId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(directUser, "direct@test.com", "hash", "Direct", "User", Now));
            db.Users.Add(ApplicationUser.Create(inheritedUser, "inherited@test.com", "hash", "Inherited", "User", Now));
            db.Users.Add(ApplicationUser.Create(overrideUser, "override@test.com", "hash", "Override", "User", Now));
            db.Users.Add(ApplicationUser.Create(unrelatedUser, "unrelated@test.com", "hash", "Unrelated", "User", Now));

            db.Roles.Add(Role.Create(roleId, $"SearchRole.{Guid.NewGuid():N}", Now));
            db.Roles.Add(Role.Create(otherRoleId, $"OtherSearchRole.{Guid.NewGuid():N}", Now));
            db.Positions.Add(Position.Create(positionId, companyId, "Search Position", Now));
            db.PositionRoles.Add(PositionRole.Create(positionId, roleId, Now));
            db.UserPositions.Add(UserPosition.Create(inheritedUser, positionId, Now));

            db.UserRoles.Add(UserRole.Create(directUser, roleId, Now));
            db.EmployeeRoleOverrides.Add(
                EmployeeRoleOverride.Create(companyId, overrideUser, roleId, EmployeeRoleOverrideType.Grant, "Cover", null, Now));
            db.UserRoles.Add(UserRole.Create(unrelatedUser, otherRoleId, Now));

            await db.SaveChangesAsync();
        }

        var handler = BuildHandler([directUser, inheritedUser, overrideUser, unrelatedUser]);

        var result = await handler.HandleAsync(
            new SearchUserAccessRequest { CompanyId = companyId, RoleId = roleId }, CancellationToken.None);

        Assert.Equal(3, result.TotalCount);
        Assert.DoesNotContain(result.Items, i => i.EmployeeId == unrelatedUser);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_PositionId()
    {
        var companyId = Guid.NewGuid();
        var inPosition = Guid.NewGuid();
        var notInPosition = Guid.NewGuid();
        var positionId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(inPosition, "inpos@test.com", "hash", "In", "Position", Now));
            db.Users.Add(ApplicationUser.Create(notInPosition, "notinpos@test.com", "hash", "Not", "InPosition", Now));
            db.Positions.Add(Position.Create(positionId, companyId, "Filter Position", Now));
            db.UserPositions.Add(UserPosition.Create(inPosition, positionId, Now));
            await db.SaveChangesAsync();
        }

        var handler = BuildHandler([inPosition, notInPosition]);

        var result = await handler.HandleAsync(
            new SearchUserAccessRequest { CompanyId = companyId, PositionId = positionId }, CancellationToken.None);

        Assert.Single(result.Items, i => i.EmployeeId == inPosition);
    }

    [Theory]
    [InlineData(OverrideStateFilter.HasGrantOverride, true, false)]
    [InlineData(OverrideStateFilter.HasDenyOverride, false, true)]
    [InlineData(OverrideStateFilter.HasAnyOverride, true, true)]
    internal async Task HandleAsync_Filters_By_OverrideState(
        OverrideStateFilter filter, bool matchesGrantUser, bool matchesDenyUser)
    {
        var companyId = Guid.NewGuid();
        var grantUser = Guid.NewGuid();
        var denyUser = Guid.NewGuid();
        var noOverrideUser = Guid.NewGuid();
        var roleId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(grantUser, $"grantstate-{grantUser:N}@test.com", "hash", "Grant", "User", Now));
            db.Users.Add(ApplicationUser.Create(denyUser, $"denystate-{denyUser:N}@test.com", "hash", "Deny", "User", Now));
            db.Users.Add(ApplicationUser.Create(noOverrideUser, $"nostate-{noOverrideUser:N}@test.com", "hash", "No", "Override", Now));
            db.Roles.Add(Role.Create(roleId, $"OverrideStateRole.{Guid.NewGuid():N}", Now));
            db.EmployeeRoleOverrides.Add(
                EmployeeRoleOverride.Create(companyId, grantUser, roleId, EmployeeRoleOverrideType.Grant, "Grant", null, Now));
            db.EmployeeRoleOverrides.Add(
                EmployeeRoleOverride.Create(companyId, denyUser, roleId, EmployeeRoleOverrideType.Deny, "Deny", null, Now));
            await db.SaveChangesAsync();
        }

        var handler = BuildHandler([grantUser, denyUser, noOverrideUser]);

        var result = await handler.HandleAsync(
            new SearchUserAccessRequest { CompanyId = companyId, OverrideState = filter }, CancellationToken.None);

        Assert.Equal(matchesGrantUser, result.Items.Any(i => i.EmployeeId == grantUser));
        Assert.Equal(matchesDenyUser, result.Items.Any(i => i.EmployeeId == denyUser));
        Assert.DoesNotContain(result.Items, i => i.EmployeeId == noOverrideUser);
    }

    [Fact]
    public async Task HandleAsync_HasExpiringOverride_Matches_Only_Overrides_Expiring_Within_14_Days()
    {
        var companyId = Guid.NewGuid();
        var expiringSoonUser = Guid.NewGuid();
        var expiringLaterUser = Guid.NewGuid();
        var noExpiryUser = Guid.NewGuid();
        var roleId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(expiringSoonUser, "soon@test.com", "hash", "Soon", "User", Now));
            db.Users.Add(ApplicationUser.Create(expiringLaterUser, "later@test.com", "hash", "Later", "User", Now));
            db.Users.Add(ApplicationUser.Create(noExpiryUser, "never@test.com", "hash", "Never", "User", Now));
            db.Roles.Add(Role.Create(roleId, $"ExpiringRole.{Guid.NewGuid():N}", Now));
            db.EmployeeRoleOverrides.Add(
                EmployeeRoleOverride.Create(companyId, expiringSoonUser, roleId, EmployeeRoleOverrideType.Grant, "Soon", Now.AddDays(13), Now));
            db.EmployeeRoleOverrides.Add(
                EmployeeRoleOverride.Create(companyId, expiringLaterUser, roleId, EmployeeRoleOverrideType.Grant, "Later", Now.AddDays(15), Now));
            db.EmployeeRoleOverrides.Add(
                EmployeeRoleOverride.Create(companyId, noExpiryUser, roleId, EmployeeRoleOverrideType.Grant, "Never", null, Now));
            await db.SaveChangesAsync();
        }

        var handler = BuildHandler([expiringSoonUser, expiringLaterUser, noExpiryUser]);

        var result = await handler.HandleAsync(
            new SearchUserAccessRequest { CompanyId = companyId, OverrideState = OverrideStateFilter.HasExpiringOverride },
            CancellationToken.None);

        Assert.Single(result.Items, i => i.EmployeeId == expiringSoonUser);
    }

    [Fact]
    public async Task HandleAsync_Search_Matches_Name_Or_Email_Case_Insensitively()
    {
        var companyId = Guid.NewGuid();
        var matchingUser = Guid.NewGuid();
        var nonMatchingUser = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(matchingUser, "ZEBRA@test.com", "hash", "Zebra", "Match", Now));
            db.Users.Add(ApplicationUser.Create(nonMatchingUser, "other@test.com", "hash", "Other", "Person", Now));
            await db.SaveChangesAsync();
        }

        var handler = BuildHandler([matchingUser, nonMatchingUser]);

        var result = await handler.HandleAsync(
            new SearchUserAccessRequest { CompanyId = companyId, Search = "zebra" }, CancellationToken.None);

        Assert.Single(result.Items, i => i.EmployeeId == matchingUser);
    }

    [Fact]
    public async Task HandleAsync_Pages_The_Filtered_Results()
    {
        var companyId = Guid.NewGuid();
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();

        await using (var db = fixture.BuildContext())
        {
            foreach (var id in ids)
                db.Users.Add(ApplicationUser.Create(id, $"{id:N}@test.com", "hash", "Paged", "User", Now));
            await db.SaveChangesAsync();
        }

        var handler = BuildHandler(ids);

        var result = await handler.HandleAsync(
            new SearchUserAccessRequest { CompanyId = companyId, Page = 2, PageSize = 2 }, CancellationToken.None);

        Assert.Equal(5, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(2, result.Page);
    }
}
