using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.AddEmployeeRoleOverride;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class AddEmployeeRoleOverrideHandlerTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly FakeClock Clock = new(Now.UtcDateTime);

    private AddEmployeeRoleOverrideHandler BuildHandler(
        FakeAuditEventPublisher auditPublisher,
        FakeTargetUserCompanyGuard? guard = null) =>
        new(
            fixture.BuildContext(),
            Clock,
            auditPublisher,
            guard ?? new FakeTargetUserCompanyGuard(),
            new IdentityAuthorizationService(fixture.BuildContext(), Clock));

    private async Task<Guid> SeedUser(string suffix)
    {
        await using var db = fixture.BuildContext();
        var userId = Guid.NewGuid();
        db.Users.Add(ApplicationUser.Create(userId, $"user-{suffix}-{userId:N}@test.com", "hash", "Test", "User", Now));
        await db.SaveChangesAsync();
        return userId;
    }

    private async Task<Guid> SeedRole(string name)
    {
        await using var db = fixture.BuildContext();
        var roleId = Guid.NewGuid();
        // Suffix with the role id so repeated calls with the same literal name (this class calls
        // SeedRole("SomeRole") from several test methods sharing one real Postgres database via
        // IdentityDatabaseFixture) never collide on the unique normalized_name index.
        db.Roles.Add(Role.Create(roleId, $"{name}-{roleId:N}", Now));
        await db.SaveChangesAsync();
        return roleId;
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
    public async Task HandleAsync_Returns_NotFound_When_Target_User_Not_A_Company_Member()
    {
        var targetUserId = await SeedUser("wrong-company");
        var roleId = await SeedRole("SomeRole");
        var actorId = Guid.NewGuid();
        await GrantSystemRole(actorId, SystemRoles.HrAdministrator);

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher, new FakeTargetUserCompanyGuard(isMember: false));

        var result = await handler.HandleAsync(
            new AddEmployeeRoleOverrideRequest
            {
                CompanyId = Guid.NewGuid(),
                UserId = targetUserId,
                RoleId = roleId,
                OverrideType = EmployeeRoleOverrideType.Grant,
                Reason = "Needed",
            },
            actorUserId: actorId,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(auditPublisher.PublishedEvents);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_RoleId_Unknown()
    {
        var targetUserId = await SeedUser("unknown-role");
        var actorId = Guid.NewGuid();
        await GrantSystemRole(actorId, SystemRoles.HrAdministrator);

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher);

        var result = await handler.HandleAsync(
            new AddEmployeeRoleOverrideRequest
            {
                CompanyId = Guid.NewGuid(),
                UserId = targetUserId,
                RoleId = Guid.NewGuid(),
                OverrideType = EmployeeRoleOverrideType.Grant,
                Reason = "Needed",
            },
            actorUserId: actorId,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Empty(auditPublisher.PublishedEvents);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_ExpiresAt_Is_Now()
    {
        // Boundary: ExpiresAt == now must be rejected — the comparison is `<=`.
        var targetUserId = await SeedUser("expires-now");
        var roleId = await SeedRole("SomeRole");
        var actorId = Guid.NewGuid();
        await GrantSystemRole(actorId, SystemRoles.HrAdministrator);

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher);

        var result = await handler.HandleAsync(
            new AddEmployeeRoleOverrideRequest
            {
                CompanyId = Guid.NewGuid(),
                UserId = targetUserId,
                RoleId = roleId,
                OverrideType = EmployeeRoleOverrideType.Grant,
                Reason = "Needed",
                ExpiresAt = Now,
            },
            actorUserId: actorId,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_ExpiresAt_In_The_Past()
    {
        var targetUserId = await SeedUser("expires-past");
        var roleId = await SeedRole("SomeRole");
        var actorId = Guid.NewGuid();
        await GrantSystemRole(actorId, SystemRoles.HrAdministrator);

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher);

        var result = await handler.HandleAsync(
            new AddEmployeeRoleOverrideRequest
            {
                CompanyId = Guid.NewGuid(),
                UserId = targetUserId,
                RoleId = roleId,
                OverrideType = EmployeeRoleOverrideType.Grant,
                Reason = "Needed",
                ExpiresAt = Now.AddSeconds(-1),
            },
            actorUserId: actorId,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Succeeds_When_ExpiresAt_Is_Strictly_In_The_Future()
    {
        var targetUserId = await SeedUser("expires-future");
        var actorId = Guid.NewGuid();
        await GrantSystemRole(actorId, SystemRoles.HrAdministrator);

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher);

        var result = await handler.HandleAsync(
            new AddEmployeeRoleOverrideRequest
            {
                CompanyId = Guid.NewGuid(),
                UserId = targetUserId,
                // Must be a role within RoleAdministrationPolicy's administrable set for the
                // actor's held role (HrAdministrator) — an arbitrary custom role would correctly
                // be rejected as unauthorised, which isn't what this ExpiresAt-boundary test cares
                // about.
                RoleId = SystemRoles.Manager,
                OverrideType = EmployeeRoleOverrideType.Grant,
                Reason = "Needed",
                ExpiresAt = Now.AddSeconds(1),
            },
            actorUserId: actorId,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Rejects_Self_Grant_As_Self_Elevation()
    {
        var actorId = await SeedUser("self-grant");
        await GrantSystemRole(actorId, SystemRoles.HrAdministrator);
        var roleId = await SeedRole("SomeRole");

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher);

        var result = await handler.HandleAsync(
            new AddEmployeeRoleOverrideRequest
            {
                CompanyId = Guid.NewGuid(),
                UserId = actorId,
                RoleId = roleId,
                OverrideType = EmployeeRoleOverrideType.Grant,
                Reason = "Trying to elevate self",
            },
            actorUserId: actorId,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("forbidden", result.Error.Code);
        Assert.Single(auditPublisher.PublishedEvents, e =>
            e is RoleChangeRejectedAuditEvent rejected && rejected.Reason == "self_elevation_denied");

        await using var db = fixture.BuildContext();
        Assert.False(await db.EmployeeRoleOverrides.AnyAsync(o => o.UserId == actorId));
    }

    [Fact]
    public async Task HandleAsync_Allows_Self_Deny()
    {
        // Negated branch of the self-elevation guard: a self-created Deny is not a privilege
        // escalation risk and must be allowed.
        var actorId = await SeedUser("self-deny");
        await GrantSystemRole(actorId, SystemRoles.HrAdministrator);
        var roleId = SystemRoles.Manager;

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher);

        var result = await handler.HandleAsync(
            new AddEmployeeRoleOverrideRequest
            {
                CompanyId = Guid.NewGuid(),
                UserId = actorId,
                RoleId = roleId,
                OverrideType = EmployeeRoleOverrideType.Deny,
                Reason = "Voluntarily restricting my own access",
            },
            actorUserId: actorId,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(auditPublisher.PublishedEvents, e => e is EmployeeRoleOverrideCreatedAuditEvent);
    }

    [Fact]
    public async Task HandleAsync_Rejects_Role_Outside_Actors_Administrable_Set()
    {
        // HR Administrator can never grant/deny Company Administrator via an override.
        var targetUserId = await SeedUser("unauthorised-target");
        var actorId = Guid.NewGuid();
        await GrantSystemRole(actorId, SystemRoles.HrAdministrator);

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher);

        var result = await handler.HandleAsync(
            new AddEmployeeRoleOverrideRequest
            {
                CompanyId = Guid.NewGuid(),
                UserId = targetUserId,
                RoleId = SystemRoles.CompanyAdministrator,
                OverrideType = EmployeeRoleOverrideType.Grant,
                Reason = "Attempting an out-of-scope grant",
            },
            actorUserId: actorId,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("forbidden", result.Error.Code);
        Assert.Single(auditPublisher.PublishedEvents, e =>
            e is RoleChangeRejectedAuditEvent rejected && rejected.Reason == "role_not_authorised_to_administer");
    }

    [Fact]
    public async Task HandleAsync_Creates_Override_And_Publishes_Audit_Event_On_Happy_Path()
    {
        var targetUserId = await SeedUser("happy-path");
        var actorId = Guid.NewGuid();
        await GrantSystemRole(actorId, SystemRoles.HrAdministrator);

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher);
        var companyId = Guid.NewGuid();

        var result = await handler.HandleAsync(
            new AddEmployeeRoleOverrideRequest
            {
                CompanyId = companyId,
                UserId = targetUserId,
                RoleId = SystemRoles.Manager,
                OverrideType = EmployeeRoleOverrideType.Grant,
                Reason = "Covering for a colleague on leave",
            },
            actorUserId: actorId,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(EmployeeRoleOverrideType.Grant, result.Value!.OverrideType);

        await using var db = fixture.BuildContext();
        var stored = await db.EmployeeRoleOverrides.SingleAsync(o => o.UserId == targetUserId);
        Assert.Equal(SystemRoles.Manager, stored.RoleId);
        Assert.Equal(EmployeeRoleOverrideType.Grant, stored.OverrideType);

        Assert.Single(auditPublisher.PublishedEvents, e =>
            e is EmployeeRoleOverrideCreatedAuditEvent created &&
            created.CompanyId == companyId &&
            created.UserId == targetUserId &&
            created.RoleId == SystemRoles.Manager &&
            created.OverrideType == EmployeeRoleOverrideType.Grant &&
            created.ActorUserId == actorId);
    }

    [Fact]
    public async Task HandleAsync_Replaces_An_Existing_Override_For_The_Same_Role_Instead_Of_Failing()
    {
        var targetUserId = await SeedUser("replace-existing");
        var actorId = Guid.NewGuid();
        await GrantSystemRole(actorId, SystemRoles.HrAdministrator);

        var auditPublisher = new FakeAuditEventPublisher();
        var companyId = Guid.NewGuid();

        // First a Deny...
        var firstHandler = BuildHandler(auditPublisher);
        var firstResult = await firstHandler.HandleAsync(
            new AddEmployeeRoleOverrideRequest
            {
                CompanyId = companyId,
                UserId = targetUserId,
                RoleId = SystemRoles.Manager,
                OverrideType = EmployeeRoleOverrideType.Deny,
                Reason = "Initial deny",
            },
            actorUserId: actorId,
            CancellationToken.None);
        Assert.True(firstResult.IsSuccess);

        Guid firstOverrideId;
        await using (var db = fixture.BuildContext())
        {
            firstOverrideId = (await db.EmployeeRoleOverrides.SingleAsync(o => o.UserId == targetUserId)).Id;
        }

        // ...then a Grant for the same role, which must replace it rather than violate the
        // unique (user_id, role_id) index.
        var secondHandler = BuildHandler(auditPublisher);
        var secondResult = await secondHandler.HandleAsync(
            new AddEmployeeRoleOverrideRequest
            {
                CompanyId = companyId,
                UserId = targetUserId,
                RoleId = SystemRoles.Manager,
                OverrideType = EmployeeRoleOverrideType.Grant,
                Reason = "Reversed decision",
            },
            actorUserId: actorId,
            CancellationToken.None);
        Assert.True(secondResult.IsSuccess);

        await using var db2 = fixture.BuildContext();
        var overrides = await db2.EmployeeRoleOverrides.Where(o => o.UserId == targetUserId).ToListAsync();
        Assert.Single(overrides);
        Assert.Equal(EmployeeRoleOverrideType.Grant, overrides[0].OverrideType);
        Assert.NotEqual(firstOverrideId, overrides[0].Id);

        Assert.Equal(2, auditPublisher.PublishedEvents.Count(e => e is EmployeeRoleOverrideCreatedAuditEvent));
    }
}
