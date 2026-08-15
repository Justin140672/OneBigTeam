using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.ResetPlatformAdministratorMfa;
using HR.Modules.Identity.Tests.Infrastructure;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class ResetPlatformAdministratorMfaHandlerTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly FakeClock Clock = new(Now.UtcDateTime);

    private ResetPlatformAdministratorMfaHandler BuildHandler(FakeAuditEventPublisher auditPublisher) =>
        new(fixture.BuildContext(), Clock, auditPublisher);

    private async Task<string> SeedOwnerAsync()
    {
        var email = $"owner-{Guid.NewGuid():N}@test.com";
        await using var db = fixture.BuildContext();
        db.PlatformAdministrators.Add(PlatformAdministrator.Create(email, PlatformAdministratorRole.PlatformOwner, Now));
        await db.SaveChangesAsync();
        return email;
    }

    private async Task<Guid> SeedTargetAsync()
    {
        await using var db = fixture.BuildContext();
        var target = PlatformAdministrator.Create(
            $"target-{Guid.NewGuid():N}@test.com", PlatformAdministratorRole.SupportStaff, Now);
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
            new ResetPlatformAdministratorMfaRequest(targetId), currentUser, CancellationToken.None);

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
            new ResetPlatformAdministratorMfaRequest(Guid.NewGuid()), currentUser, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Not_Implemented_Stub_Response_And_Publishes_Audit_Event_On_Happy_Path()
    {
        var ownerEmail = await SeedOwnerAsync();
        var targetId = await SeedTargetAsync();
        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher);
        var currentUser = new FakeCurrentUser(Guid.NewGuid(), ownerEmail);

        var result = await handler.HandleAsync(
            new ResetPlatformAdministratorMfaRequest(targetId), currentUser, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(targetId, result.Value.AdministratorId);
        Assert.False(result.Value.Implemented);

        Assert.Single(auditPublisher.PublishedEvents, e => e is PlatformAdministratorMfaResetRequestedAuditEvent);
    }
}
