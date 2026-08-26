using HR.Modules.Identity.Authorization;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.UpdateUserRoles;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class UpdateUserRolesHandlerTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly FakeClock Clock = new(Now.UtcDateTime);

    private UpdateUserRolesHandler BuildHandler(
        FakeAuditEventPublisher auditPublisher,
        FakeTargetUserCompanyGuard? guard = null,
        IReadOnlyList<Guid>? companyEmployeeIds = null) =>
        new(
            fixture.BuildContext(),
            Clock,
            auditPublisher,
            guard ?? new FakeTargetUserCompanyGuard(),
            new IdentityAuthorizationService(fixture.BuildContext(), Clock),
            new FakeEmployeeAudienceReader(companyEmployeeIds ?? []),
            new LastActiveAdministratorGuard(fixture.BuildContext(), new FakeEmployeeAudienceReader(companyEmployeeIds ?? [])));

    private async Task<Guid> SeedUser(string suffix)
    {
        await using var db = fixture.BuildContext();
        var userId = Guid.NewGuid();
        db.Users.Add(ApplicationUser.Create(userId, $"user{suffix}@test.com", "hash", "Test", "User", Now));
        await db.SaveChangesAsync();
        return userId;
    }

    private async Task<Guid> SeedRole(string name)
    {
        await using var db = fixture.BuildContext();
        var roleId = Guid.NewGuid();
        db.Roles.Add(Role.Create(roleId, name, Now));
        await db.SaveChangesAsync();
        return roleId;
    }

    /// <summary>Grants a system role (HrAdministrator/CompanyAdministrator) directly to the given user so
    /// they're recognised as an administrator by <see cref="RoleAdministrationPolicy"/>.</summary>
    private async Task GrantSystemRole(Guid userId, Guid systemRoleId)
    {
        await using var db = fixture.BuildContext();
        if (!await db.Roles.AnyAsync(r => r.Id == systemRoleId))
            db.Roles.Add(Role.Create(systemRoleId, systemRoleId.ToString(), Now));
        db.UserRoles.Add(UserRole.Create(userId, systemRoleId, Now));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_User_Missing()
    {
        var handler = BuildHandler(new FakeAuditEventPublisher());

        var result = await handler.HandleAsync(
            new UpdateUserRolesRequest { CompanyId = Guid.NewGuid(), UserId = Guid.NewGuid(), RoleIds = [SystemRoles.Employee] },
            actorUserId: null,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_RoleId_Unknown()
    {
        var userId = await SeedUser("unknown-role");
        var handler = BuildHandler(new FakeAuditEventPublisher());

        var result = await handler.HandleAsync(
            new UpdateUserRolesRequest { CompanyId = Guid.NewGuid(), UserId = userId, RoleIds = [Guid.NewGuid()] },
            actorUserId: null,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_RoleIds_Empty()
    {
        var userId = await SeedUser("empty-roles");
        var handler = BuildHandler(new FakeAuditEventPublisher());

        var result = await handler.HandleAsync(
            new UpdateUserRolesRequest { CompanyId = Guid.NewGuid(), UserId = userId, RoleIds = [] },
            actorUserId: null,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_Employee_Role_Removed()
    {
        var userId = await SeedUser("remove-employee-role");
        var newRoleId = await SeedRole("SomeOtherRole");
        var actorId = Guid.NewGuid();
        await GrantSystemRole(actorId, SystemRoles.HrAdministrator);

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher);

        var result = await handler.HandleAsync(
            new UpdateUserRolesRequest { CompanyId = Guid.NewGuid(), UserId = userId, RoleIds = [newRoleId] },
            actorUserId: actorId,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Single(auditPublisher.PublishedEvents, e => e is RoleChangeRejectedAuditEvent);
    }

    [Fact]
    public async Task HandleAsync_Replaces_Role_Set_And_Publishes_Audit_Event_When_Changed()
    {
        var userId = await SeedUser("replace-roles");
        var actorId = Guid.NewGuid();
        await GrantSystemRole(actorId, SystemRoles.HrAdministrator);

        await using (var db = fixture.BuildContext())
        {
            db.UserRoles.Add(UserRole.Create(userId, SystemRoles.Manager, Now));
            await db.SaveChangesAsync();
        }

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher);

        var result = await handler.HandleAsync(
            new UpdateUserRolesRequest { CompanyId = Guid.NewGuid(), UserId = userId, RoleIds = [SystemRoles.Employee, SystemRoles.Recruiter] },
            actorUserId: actorId,
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        await using var db2 = fixture.BuildContext();
        var roles = await db2.UserRoles.Where(ur => ur.UserId == userId).Select(ur => ur.RoleId).ToListAsync();
        Assert.Equal(2, roles.Count);
        Assert.Contains(SystemRoles.Recruiter, roles);
        Assert.DoesNotContain(SystemRoles.Manager, roles);

        Assert.Single(auditPublisher.PublishedEvents, e => e is UserRolesChangedAuditEvent);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Publish_Audit_Event_When_Role_Set_Unchanged()
    {
        var userId = await SeedUser("unchanged-roles");
        var actorId = Guid.NewGuid();
        await GrantSystemRole(actorId, SystemRoles.HrAdministrator);

        await using (var db = fixture.BuildContext())
        {
            db.UserRoles.Add(UserRole.Create(userId, SystemRoles.Employee, Now));
            await db.SaveChangesAsync();
        }

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher);

        var result = await handler.HandleAsync(
            new UpdateUserRolesRequest { CompanyId = Guid.NewGuid(), UserId = userId, RoleIds = [SystemRoles.Employee] },
            actorUserId: actorId,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(auditPublisher.PublishedEvents);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_And_Does_Not_Change_Roles_When_Guard_Reports_Not_A_Member()
    {
        var userId = await SeedUser("cross-tenant");
        var actorId = Guid.NewGuid();
        await GrantSystemRole(actorId, SystemRoles.HrAdministrator);

        await using (var db = fixture.BuildContext())
        {
            db.UserRoles.Add(UserRole.Create(userId, SystemRoles.Manager, Now));
            await db.SaveChangesAsync();
        }

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher, new FakeTargetUserCompanyGuard(isMember: false));

        var result = await handler.HandleAsync(
            new UpdateUserRolesRequest { CompanyId = Guid.NewGuid(), UserId = userId, RoleIds = [SystemRoles.Employee, SystemRoles.Recruiter] },
            actorUserId: actorId,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);

        await using var db2 = fixture.BuildContext();
        var roles = await db2.UserRoles.Where(ur => ur.UserId == userId).Select(ur => ur.RoleId).ToListAsync();
        Assert.Single(roles);
        Assert.Contains(SystemRoles.Manager, roles); // untouched — guard short-circuited before any read/write

        Assert.Empty(auditPublisher.PublishedEvents);
    }

    [Fact]
    public async Task HandleAsync_Rejects_Self_Elevation_When_HrAdministrator_Grants_Self_CompanyAdministrator()
    {
        var actorId = await SeedUser("self-elevate");
        await GrantSystemRole(actorId, SystemRoles.HrAdministrator);
        await GrantSystemRole(actorId, SystemRoles.Employee);

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher);

        var result = await handler.HandleAsync(
            new UpdateUserRolesRequest
            {
                CompanyId = Guid.NewGuid(),
                UserId = actorId,
                RoleIds = [SystemRoles.Employee, SystemRoles.HrAdministrator, SystemRoles.CompanyAdministrator],
            },
            actorUserId: actorId,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("forbidden", result.Error.Code);
        Assert.Single(auditPublisher.PublishedEvents, e =>
            e is RoleChangeRejectedAuditEvent rejected && rejected.Reason == "self_elevation_denied");

        await using var db = fixture.BuildContext();
        var roles = await db.UserRoles.Where(ur => ur.UserId == actorId).Select(ur => ur.RoleId).ToListAsync();
        Assert.DoesNotContain(SystemRoles.CompanyAdministrator, roles);
    }

    [Fact]
    public async Task HandleAsync_Rejects_CompanyAdministrator_Granting_Themselves_HrAdministrator()
    {
        var actorId = await SeedUser("company-admin-self-grant-hr");
        await GrantSystemRole(actorId, SystemRoles.CompanyAdministrator);
        await GrantSystemRole(actorId, SystemRoles.Employee);

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher);

        var result = await handler.HandleAsync(
            new UpdateUserRolesRequest
            {
                CompanyId = Guid.NewGuid(),
                UserId = actorId,
                RoleIds = [SystemRoles.Employee, SystemRoles.CompanyAdministrator, SystemRoles.HrAdministrator],
            },
            actorUserId: actorId,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("forbidden", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Rejects_Role_Grant_The_Actor_Is_Not_Authorised_To_Administer()
    {
        var targetUserId = await SeedUser("unauthorised-target");
        var actorId = Guid.NewGuid();
        await GrantSystemRole(actorId, SystemRoles.Manager); // Manager cannot administer any roles

        await using (var db = fixture.BuildContext())
        {
            db.UserRoles.Add(UserRole.Create(targetUserId, SystemRoles.Employee, Now));
            await db.SaveChangesAsync();
        }

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher);

        var result = await handler.HandleAsync(
            new UpdateUserRolesRequest
            {
                CompanyId = Guid.NewGuid(),
                UserId = targetUserId,
                RoleIds = [SystemRoles.Employee, SystemRoles.Recruiter],
            },
            actorUserId: actorId,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("forbidden", result.Error.Code);
        Assert.Single(auditPublisher.PublishedEvents, e =>
            e is RoleChangeRejectedAuditEvent rejected && rejected.Reason == "role_not_authorised_to_administer");
    }

    [Fact]
    public async Task HandleAsync_Rejects_Removing_The_Last_Active_CompanyAdministrator()
    {
        var companyId = Guid.NewGuid();
        var targetUserId = await SeedUser("last-company-admin");
        var actorId = Guid.NewGuid();
        await GrantSystemRole(actorId, SystemRoles.CompanyAdministrator);

        await using (var db = fixture.BuildContext())
        {
            db.UserRoles.Add(UserRole.Create(targetUserId, SystemRoles.Employee, Now));
            db.UserRoles.Add(UserRole.Create(targetUserId, SystemRoles.CompanyAdministrator, Now));
            await db.SaveChangesAsync();
        }

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher, companyEmployeeIds: [targetUserId]);

        var result = await handler.HandleAsync(
            new UpdateUserRolesRequest { CompanyId = companyId, UserId = targetUserId, RoleIds = [SystemRoles.Employee] },
            actorUserId: actorId,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Single(auditPublisher.PublishedEvents, e =>
            e is RoleChangeRejectedAuditEvent rejected && rejected.Reason == "last_active_administrator");

        await using var db2 = fixture.BuildContext();
        var roles = await db2.UserRoles.Where(ur => ur.UserId == targetUserId).Select(ur => ur.RoleId).ToListAsync();
        Assert.Contains(SystemRoles.CompanyAdministrator, roles); // untouched
    }

    [Fact]
    public async Task HandleAsync_Allows_Removing_CompanyAdministrator_When_Another_Active_Holder_Exists()
    {
        var companyId = Guid.NewGuid();
        var targetUserId = await SeedUser("removable-company-admin");
        var otherAdminId = await SeedUser("other-company-admin");
        var actorId = Guid.NewGuid();
        await GrantSystemRole(actorId, SystemRoles.CompanyAdministrator);

        await using (var db = fixture.BuildContext())
        {
            db.UserRoles.Add(UserRole.Create(targetUserId, SystemRoles.Employee, Now));
            db.UserRoles.Add(UserRole.Create(targetUserId, SystemRoles.CompanyAdministrator, Now));
            db.UserRoles.Add(UserRole.Create(otherAdminId, SystemRoles.CompanyAdministrator, Now));
            await db.SaveChangesAsync();
        }

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher, companyEmployeeIds: [targetUserId, otherAdminId]);

        var result = await handler.HandleAsync(
            new UpdateUserRolesRequest { CompanyId = companyId, UserId = targetUserId, RoleIds = [SystemRoles.Employee] },
            actorUserId: actorId,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Allows_Multi_Role_Holder_To_Lose_One_Protected_Role_Without_Coupling_To_The_Other()
    {
        // Mirrors the SignUp initial-company-creator shape: Employee + CompanyAdministrator + HrAdministrator.
        var companyId = Guid.NewGuid();
        var targetUserId = await SeedUser("initial-creator");
        var otherHrAdminId = await SeedUser("other-hr-admin");
        var actorId = Guid.NewGuid();
        await GrantSystemRole(actorId, SystemRoles.HrAdministrator);

        await using (var db = fixture.BuildContext())
        {
            db.UserRoles.Add(UserRole.Create(targetUserId, SystemRoles.Employee, Now));
            db.UserRoles.Add(UserRole.Create(targetUserId, SystemRoles.CompanyAdministrator, Now));
            db.UserRoles.Add(UserRole.Create(targetUserId, SystemRoles.HrAdministrator, Now));
            db.UserRoles.Add(UserRole.Create(otherHrAdminId, SystemRoles.HrAdministrator, Now));
            await db.SaveChangesAsync();
        }

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher, companyEmployeeIds: [targetUserId, otherHrAdminId]);

        // Remove only HrAdministrator — CompanyAdministrator (held only by this user) must stay,
        // since actor (HrAdministrator) cannot administer that role anyway.
        var result = await handler.HandleAsync(
            new UpdateUserRolesRequest
            {
                CompanyId = companyId,
                UserId = targetUserId,
                RoleIds = [SystemRoles.Employee, SystemRoles.CompanyAdministrator],
            },
            actorUserId: actorId,
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        await using var db2 = fixture.BuildContext();
        var roles = await db2.UserRoles.Where(ur => ur.UserId == targetUserId).Select(ur => ur.RoleId).ToListAsync();
        Assert.Contains(SystemRoles.CompanyAdministrator, roles);
        Assert.DoesNotContain(SystemRoles.HrAdministrator, roles);
    }
}
