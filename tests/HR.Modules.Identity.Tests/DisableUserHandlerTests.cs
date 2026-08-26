using HR.Modules.Identity.Authorization;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.DisableUser;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class DisableUserHandlerTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly FakeClock Clock = new(Now.UtcDateTime);

    private DisableUserHandler BuildHandler(
        FakeAuditEventPublisher auditPublisher,
        FakeTargetUserCompanyGuard? guard = null,
        IReadOnlyList<Guid>? companyEmployeeIds = null) =>
        new(
            fixture.BuildContext(),
            Clock,
            auditPublisher,
            guard ?? new FakeTargetUserCompanyGuard(),
            new LastActiveAdministratorGuard(fixture.BuildContext(), new FakeEmployeeAudienceReader(companyEmployeeIds ?? [])));

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_User_Missing()
    {
        var handler = BuildHandler(new FakeAuditEventPublisher());

        var result = await handler.HandleAsync(
            new DisableUserRequest { CompanyId = Guid.NewGuid(), UserId = Guid.NewGuid() },
            actorUserId: null,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Already_Disabled()
    {
        var userId = Guid.NewGuid();
        await using (var db = fixture.BuildContext())
        {
            var user = ApplicationUser.Create(userId, $"disabled-{userId}@test.com", "hash", "Test", "User", Now);
            user.Deactivate(Now);
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        var handler = BuildHandler(new FakeAuditEventPublisher());

        var result = await handler.HandleAsync(
            new DisableUserRequest { CompanyId = Guid.NewGuid(), UserId = userId },
            actorUserId: null,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Disables_User_And_Publishes_Audit_Event_On_Happy_Path()
    {
        var userId = Guid.NewGuid();
        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(userId, $"active-{userId}@test.com", "hash", "Test", "User", Now));
            await db.SaveChangesAsync();
        }

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher);

        var result = await handler.HandleAsync(
            new DisableUserRequest { CompanyId = Guid.NewGuid(), UserId = userId },
            actorUserId: Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsActive);

        await using var db2 = fixture.BuildContext();
        var reloaded = await db2.Users.FirstAsync(u => u.Id == userId);
        Assert.False(reloaded.IsActive);

        Assert.Single(auditPublisher.PublishedEvents, e => e is UserDisabledAuditEvent);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_And_Does_Not_Disable_User_When_Guard_Reports_Not_A_Member()
    {
        var userId = Guid.NewGuid();
        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(userId, $"cross-tenant-{userId}@test.com", "hash", "Test", "User", Now));
            await db.SaveChangesAsync();
        }

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher, new FakeTargetUserCompanyGuard(isMember: false));

        var result = await handler.HandleAsync(
            new DisableUserRequest { CompanyId = Guid.NewGuid(), UserId = userId },
            actorUserId: Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);

        await using var db2 = fixture.BuildContext();
        var reloaded = await db2.Users.FirstAsync(u => u.Id == userId);
        Assert.True(reloaded.IsActive); // untouched — guard short-circuited before any read/write

        Assert.Empty(auditPublisher.PublishedEvents);
    }

    [Fact]
    public async Task HandleAsync_Rejects_Disabling_The_Last_Active_CompanyAdministrator()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(userId, $"last-admin-{userId}@test.com", "hash", "Test", "User", Now));
            db.UserRoles.Add(UserRole.Create(userId, SystemRoles.CompanyAdministrator, Now));
            await db.SaveChangesAsync();
        }

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher, companyEmployeeIds: [userId]);

        var result = await handler.HandleAsync(
            new DisableUserRequest { CompanyId = companyId, UserId = userId },
            actorUserId: Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Single(auditPublisher.PublishedEvents, e => e is RoleChangeRejectedAuditEvent);

        await using var db2 = fixture.BuildContext();
        var reloaded = await db2.Users.FirstAsync(u => u.Id == userId);
        Assert.True(reloaded.IsActive); // untouched
    }

    [Fact]
    public async Task HandleAsync_Allows_Disabling_CompanyAdministrator_When_Another_Active_Holder_Exists()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var otherAdminId = Guid.NewGuid();
        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(userId, $"disable-me-{userId}@test.com", "hash", "Test", "User", Now));
            db.Users.Add(ApplicationUser.Create(otherAdminId, $"other-admin-{otherAdminId}@test.com", "hash", "Test", "User", Now));
            db.UserRoles.Add(UserRole.Create(userId, SystemRoles.CompanyAdministrator, Now));
            db.UserRoles.Add(UserRole.Create(otherAdminId, SystemRoles.CompanyAdministrator, Now));
            await db.SaveChangesAsync();
        }

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher, companyEmployeeIds: [userId, otherAdminId]);

        var result = await handler.HandleAsync(
            new DisableUserRequest { CompanyId = companyId, UserId = userId },
            actorUserId: Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }
}
