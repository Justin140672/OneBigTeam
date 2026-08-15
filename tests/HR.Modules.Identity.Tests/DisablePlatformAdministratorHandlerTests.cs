using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.DisablePlatformAdministrator;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class DisablePlatformAdministratorHandlerTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly FakeClock Clock = new(Now.UtcDateTime);

    private DisablePlatformAdministratorHandler BuildHandler(FakeAuditEventPublisher auditPublisher) =>
        new(fixture.BuildContext(), Clock, auditPublisher);

    private async Task<string> SeedOwnerAsync()
    {
        var email = $"owner-{Guid.NewGuid():N}@test.com";
        await using var db = fixture.BuildContext();
        db.PlatformAdministrators.Add(PlatformAdministrator.Create(email, PlatformAdministratorRole.PlatformOwner, Now));
        await db.SaveChangesAsync();
        return email;
    }

    private async Task<Guid> SeedTargetAsync(bool isEnabled = true)
    {
        await using var db = fixture.BuildContext();
        var target = PlatformAdministrator.Create(
            $"target-{Guid.NewGuid():N}@test.com", PlatformAdministratorRole.SupportStaff, Now);
        if (!isEnabled)
            target.Disable(Now, actorUserId: null);
        db.PlatformAdministrators.Add(target);
        await db.SaveChangesAsync();
        return target.Id;
    }

    [Fact]
    public async Task HandleAsync_Returns_Unauthorized_When_Caller_Is_Not_A_PlatformOwner()
    {
        var targetId = await SeedTargetAsync();
        var handler = BuildHandler(new FakeAuditEventPublisher());
        var currentUser = new FakeCurrentUser(Guid.NewGuid(), "not-an-owner@test.com");

        var result = await handler.HandleAsync(
            new DisablePlatformAdministratorRequest(targetId), currentUser, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unauthorized", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Administrator_Missing()
    {
        var ownerEmail = await SeedOwnerAsync();
        var handler = BuildHandler(new FakeAuditEventPublisher());
        var currentUser = new FakeCurrentUser(Guid.NewGuid(), ownerEmail);

        var result = await handler.HandleAsync(
            new DisablePlatformAdministratorRequest(Guid.NewGuid()), currentUser, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Already_Disabled()
    {
        var ownerEmail = await SeedOwnerAsync();
        var targetId = await SeedTargetAsync(isEnabled: false);
        var handler = BuildHandler(new FakeAuditEventPublisher());
        var currentUser = new FakeCurrentUser(Guid.NewGuid(), ownerEmail);

        var result = await handler.HandleAsync(
            new DisablePlatformAdministratorRequest(targetId), currentUser, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Disables_Administrator_And_Publishes_Audit_Event_On_Happy_Path()
    {
        var ownerEmail = await SeedOwnerAsync();
        var targetId = await SeedTargetAsync();
        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher);
        var currentUser = new FakeCurrentUser(Guid.NewGuid(), ownerEmail);

        var result = await handler.HandleAsync(
            new DisablePlatformAdministratorRequest(targetId), currentUser, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsEnabled);

        await using var db2 = fixture.BuildContext();
        var reloaded = await db2.PlatformAdministrators.FirstAsync(a => a.Id == targetId);
        Assert.False(reloaded.IsEnabled);

        Assert.Single(auditPublisher.PublishedEvents, e => e is PlatformAdministratorDisabledAuditEvent);
    }
}
