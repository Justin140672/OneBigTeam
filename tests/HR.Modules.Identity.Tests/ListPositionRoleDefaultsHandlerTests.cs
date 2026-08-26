using HR.Modules.Employees.Contracts;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.ListPositionRoleDefaults;
using HR.Modules.Identity.Tests.Infrastructure;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class ListPositionRoleDefaultsHandlerTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_Empty_When_No_Active_Profiles_In_Company()
    {
        var companyId = Guid.NewGuid();
        var reader = new FakePositionProfileReader(allActiveIds: []);

        await using var db = fixture.BuildContext();
        var handler = new ListPositionRoleDefaultsHandler(db, reader);

        var result = await handler.HandleAsync(new ListPositionRoleDefaultsRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Positions);
    }

    [Fact]
    public async Task HandleAsync_Returns_Active_Profiles_With_Configured_RoleIds_And_Empty_For_Unconfigured()
    {
        var companyId = Guid.NewGuid();
        var configuredPositionId = Guid.NewGuid();
        var unconfiguredPositionId = Guid.NewGuid();

        var summaries = new Dictionary<Guid, PositionProfileSummary>
        {
            [configuredPositionId] = new(configuredPositionId, "Software Developer", null, null, true, null, null),
            [unconfiguredPositionId] = new(unconfiguredPositionId, "Office Manager", null, null, true, null, null),
        };
        var reader = new FakePositionProfileReader(
            allActiveIds: [configuredPositionId, unconfiguredPositionId],
            summaries: summaries);

        await using (var db = fixture.BuildContext())
        {
            db.Positions.Add(Position.Create(configuredPositionId, companyId, "Software Developer", Now));
            db.PositionRoles.Add(PositionRole.Create(configuredPositionId, SystemRoles.Employee, Now));
            db.PositionRoles.Add(PositionRole.Create(configuredPositionId, SystemRoles.Recruiter, Now));
            await db.SaveChangesAsync();
        }

        await using var db2 = fixture.BuildContext();
        var handler = new ListPositionRoleDefaultsHandler(db2, reader);

        var result = await handler.HandleAsync(new ListPositionRoleDefaultsRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Positions.Count);

        var configured = Assert.Single(result.Value.Positions, p => p.PositionProfileId == configuredPositionId);
        Assert.Equal("Software Developer", configured.Title);
        Assert.True(configured.IsActive);
        Assert.Equal(2, configured.RoleIds.Count);
        Assert.Contains(SystemRoles.Employee, configured.RoleIds);
        Assert.Contains(SystemRoles.Recruiter, configured.RoleIds);

        var unconfigured = Assert.Single(result.Value.Positions, p => p.PositionProfileId == unconfiguredPositionId);
        Assert.Empty(unconfigured.RoleIds);
    }
}
