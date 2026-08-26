using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.EnableUser;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class EnableUserHandlerTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly FakeClock Clock = new(Now.UtcDateTime);

    private EnableUserHandler BuildHandler(FakeAuditEventPublisher auditPublisher, FakeTargetUserCompanyGuard? guard = null) =>
        new(fixture.BuildContext(), Clock, auditPublisher, guard ?? new FakeTargetUserCompanyGuard());

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_User_Missing()
    {
        var handler = BuildHandler(new FakeAuditEventPublisher());

        var result = await handler.HandleAsync(
            new EnableUserRequest { CompanyId = Guid.NewGuid(), UserId = Guid.NewGuid() },
            actorUserId: null,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Already_Active()
    {
        var userId = Guid.NewGuid();
        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(userId, $"active-{userId}@test.com", "hash", "Test", "User", Now));
            await db.SaveChangesAsync();
        }

        var handler = BuildHandler(new FakeAuditEventPublisher());

        var result = await handler.HandleAsync(
            new EnableUserRequest { CompanyId = Guid.NewGuid(), UserId = userId },
            actorUserId: null,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Enables_User_And_Publishes_Audit_Event_On_Happy_Path()
    {
        var userId = Guid.NewGuid();
        await using (var db = fixture.BuildContext())
        {
            var user = ApplicationUser.Create(userId, $"disabled-{userId}@test.com", "hash", "Test", "User", Now);
            user.Deactivate(Now);
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher);

        var result = await handler.HandleAsync(
            new EnableUserRequest { CompanyId = Guid.NewGuid(), UserId = userId },
            actorUserId: Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsActive);

        await using var db2 = fixture.BuildContext();
        var reloaded = await db2.Users.FirstAsync(u => u.Id == userId);
        Assert.True(reloaded.IsActive);

        Assert.Single(auditPublisher.PublishedEvents, e => e is UserEnabledAuditEvent);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_And_Does_Not_Enable_User_When_Guard_Reports_Not_A_Member()
    {
        var userId = Guid.NewGuid();
        await using (var db = fixture.BuildContext())
        {
            var user = ApplicationUser.Create(userId, $"cross-tenant-{userId}@test.com", "hash", "Test", "User", Now);
            user.Deactivate(Now);
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher, new FakeTargetUserCompanyGuard(isMember: false));

        var result = await handler.HandleAsync(
            new EnableUserRequest { CompanyId = Guid.NewGuid(), UserId = userId },
            actorUserId: Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);

        await using var db2 = fixture.BuildContext();
        var reloaded = await db2.Users.FirstAsync(u => u.Id == userId);
        Assert.False(reloaded.IsActive); // untouched — guard short-circuited before any read/write

        Assert.Empty(auditPublisher.PublishedEvents);
    }
}
