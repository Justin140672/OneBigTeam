using HR.Modules.Employees.Contracts;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.SetPositionRoleDefaults;
using HR.Modules.Identity.Services;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class SetPositionRoleDefaultsHandlerTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly FakeClock Clock = new(Now.UtcDateTime);

    private SetPositionRoleDefaultsHandler BuildHandler(
        FakeAuditEventPublisher auditPublisher,
        Guid companyId,
        Guid positionProfileId,
        string title = "Software Developer",
        bool profileExists = true)
    {
        var db = fixture.BuildContext();
        var summaries = new Dictionary<Guid, PositionProfileSummary>
        {
            [positionProfileId] = new(positionProfileId, title, null, null, true, null, null),
        };
        var reader = new FakePositionProfileReader(
            exists: profileExists,
            matchingCompanyId: profileExists ? companyId : null,
            matchingPositionProfileId: profileExists ? positionProfileId : null,
            summaries: summaries);

        return new SetPositionRoleDefaultsHandler(
            db,
            Clock,
            auditPublisher,
            new IdentityAuthorizationService(db, Clock),
            reader,
            new PositionSync(db, reader));
    }

    private async Task<Guid> SeedRole(string name)
    {
        await using var db = fixture.BuildContext();
        var roleId = Guid.NewGuid();
        db.Roles.Add(Role.Create(roleId, name, Now));
        await db.SaveChangesAsync();
        return roleId;
    }

    private async Task<Guid> SeedUser(string suffix)
    {
        await using var db = fixture.BuildContext();
        var userId = Guid.NewGuid();
        db.Users.Add(ApplicationUser.Create(userId, $"user{suffix}@test.com", "hash", "Test", "User", Now));
        await db.SaveChangesAsync();
        return userId;
    }

    private async Task GrantSystemRole(Guid userId, Guid systemRoleId)
    {
        await using var db = fixture.BuildContext();
        if (!await db.Roles.AnyAsync(r => r.Id == systemRoleId))
            db.Roles.Add(Role.Create(systemRoleId, systemRoleId.ToString(), Now));
        db.UserRoles.Add(UserRole.Create(userId, systemRoleId, Now));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Position_Profile_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();
        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher, companyId, positionProfileId, profileExists: false);

        var result = await handler.HandleAsync(
            new SetPositionRoleDefaultsRequest { CompanyId = companyId, PositionProfileId = positionProfileId, RoleIds = [SystemRoles.Employee] },
            actorUserId: null,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(auditPublisher.PublishedEvents);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_RoleId_Unknown()
    {
        var companyId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        await GrantSystemRole(actorId, SystemRoles.HrAdministrator);

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher, companyId, positionProfileId);

        var result = await handler.HandleAsync(
            new SetPositionRoleDefaultsRequest { CompanyId = companyId, PositionProfileId = positionProfileId, RoleIds = [Guid.NewGuid()] },
            actorUserId: actorId,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Rejects_When_Actor_Cannot_Administer_The_Requested_Role()
    {
        // HR Administrator must not be able to make Company Administrator a position default.
        var companyId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        await GrantSystemRole(actorId, SystemRoles.HrAdministrator);

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher, companyId, positionProfileId);

        var result = await handler.HandleAsync(
            new SetPositionRoleDefaultsRequest { CompanyId = companyId, PositionProfileId = positionProfileId, RoleIds = [SystemRoles.CompanyAdministrator] },
            actorUserId: actorId,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("forbidden", result.Error.Code);
        Assert.Empty(auditPublisher.PublishedEvents);

        await using var db = fixture.BuildContext();
        Assert.False(await db.PositionRoles.AnyAsync(pr => pr.PositionId == positionProfileId));
    }

    [Fact]
    public async Task HandleAsync_Adds_And_Removes_Roles_And_Publishes_Audit_Event()
    {
        var companyId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        await GrantSystemRole(actorId, SystemRoles.HrAdministrator);
        var recruiterRoleId = SystemRoles.Recruiter;

        await using (var db = fixture.BuildContext())
        {
            db.Positions.Add(Position.Create(positionProfileId, companyId, "Software Developer", Now));
            db.PositionRoles.Add(PositionRole.Create(positionProfileId, SystemRoles.Manager, Now));
            await db.SaveChangesAsync();
        }

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher, companyId, positionProfileId);

        var result = await handler.HandleAsync(
            new SetPositionRoleDefaultsRequest { CompanyId = companyId, PositionProfileId = positionProfileId, RoleIds = [SystemRoles.Employee, recruiterRoleId] },
            actorUserId: actorId,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.RoleIds.Count);

        await using var db2 = fixture.BuildContext();
        var roleIds = await db2.PositionRoles.Where(pr => pr.PositionId == positionProfileId).Select(pr => pr.RoleId).ToListAsync();
        Assert.Contains(SystemRoles.Employee, roleIds);
        Assert.Contains(recruiterRoleId, roleIds);
        Assert.DoesNotContain(SystemRoles.Manager, roleIds);

        Assert.Single(auditPublisher.PublishedEvents, e => e is PositionRoleDefaultsChangedAuditEvent);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Publish_Audit_Event_When_Requested_Roles_Equal_Current()
    {
        var companyId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        await GrantSystemRole(actorId, SystemRoles.HrAdministrator);

        await using (var db = fixture.BuildContext())
        {
            db.Positions.Add(Position.Create(positionProfileId, companyId, "Software Developer", Now));
            db.PositionRoles.Add(PositionRole.Create(positionProfileId, SystemRoles.Employee, Now));
            await db.SaveChangesAsync();
        }

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher, companyId, positionProfileId);

        var result = await handler.HandleAsync(
            new SetPositionRoleDefaultsRequest { CompanyId = companyId, PositionProfileId = positionProfileId, RoleIds = [SystemRoles.Employee] },
            actorUserId: actorId,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(auditPublisher.PublishedEvents);
    }

    [Fact]
    public async Task HandleAsync_Is_Idempotent_On_Repeat_Apply_Of_Same_Role_Set()
    {
        var companyId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        await GrantSystemRole(actorId, SystemRoles.HrAdministrator);

        var firstAuditPublisher = new FakeAuditEventPublisher();
        var firstHandler = BuildHandler(firstAuditPublisher, companyId, positionProfileId);

        var firstResult = await firstHandler.HandleAsync(
            new SetPositionRoleDefaultsRequest { CompanyId = companyId, PositionProfileId = positionProfileId, RoleIds = [SystemRoles.Employee, SystemRoles.Manager] },
            actorUserId: actorId,
            CancellationToken.None);
        Assert.True(firstResult.IsSuccess);
        Assert.Single(firstAuditPublisher.PublishedEvents);

        var secondAuditPublisher = new FakeAuditEventPublisher();
        var secondHandler = BuildHandler(secondAuditPublisher, companyId, positionProfileId);

        var secondResult = await secondHandler.HandleAsync(
            new SetPositionRoleDefaultsRequest { CompanyId = companyId, PositionProfileId = positionProfileId, RoleIds = [SystemRoles.Employee, SystemRoles.Manager] },
            actorUserId: actorId,
            CancellationToken.None);

        Assert.True(secondResult.IsSuccess);
        Assert.Empty(secondAuditPublisher.PublishedEvents); // re-applying the same set is a no-op
    }
}
