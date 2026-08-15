using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.CreatePlatformAdministrator;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class CreatePlatformAdministratorHandlerTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly FakeClock Clock = new(Now.UtcDateTime);

    private CreatePlatformAdministratorHandler BuildHandler(FakeAuditEventPublisher auditPublisher) =>
        new(fixture.BuildContext(), Clock, auditPublisher);

    private async Task SeedOwnerAsync(string email, bool isEnabled = true)
    {
        await using var db = fixture.BuildContext();
        var owner = PlatformAdministrator.Create(email, PlatformAdministratorRole.PlatformOwner, Now);
        if (!isEnabled)
            owner.Disable(Now, actorUserId: null);
        db.PlatformAdministrators.Add(owner);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task HandleAsync_Returns_Unauthorized_When_Caller_Is_Not_A_PlatformOwner()
    {
        var handler = BuildHandler(new FakeAuditEventPublisher());
        var currentUser = new FakeCurrentUser(Guid.NewGuid(), "not-an-owner@test.com");

        var result = await handler.HandleAsync(
            new CreatePlatformAdministratorRequest("new-admin@test.com", PlatformAdministratorRole.SupportStaff),
            currentUser,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unauthorized", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Unauthorized_When_Caller_Is_A_Disabled_PlatformOwner()
    {
        var ownerEmail = $"disabled-owner-{Guid.NewGuid():N}@test.com";
        await SeedOwnerAsync(ownerEmail, isEnabled: false);

        var handler = BuildHandler(new FakeAuditEventPublisher());
        var currentUser = new FakeCurrentUser(Guid.NewGuid(), ownerEmail);

        var result = await handler.HandleAsync(
            new CreatePlatformAdministratorRequest("new-admin2@test.com", PlatformAdministratorRole.SupportStaff),
            currentUser,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unauthorized", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Email_Already_Exists()
    {
        var ownerEmail = $"owner-{Guid.NewGuid():N}@test.com";
        await SeedOwnerAsync(ownerEmail);

        var existingEmail = $"existing-{Guid.NewGuid():N}@test.com";
        await using (var db = fixture.BuildContext())
        {
            db.PlatformAdministrators.Add(
                PlatformAdministrator.Create(existingEmail, PlatformAdministratorRole.SupportStaff, Now));
            await db.SaveChangesAsync();
        }

        var handler = BuildHandler(new FakeAuditEventPublisher());
        var currentUser = new FakeCurrentUser(Guid.NewGuid(), ownerEmail);

        var result = await handler.HandleAsync(
            new CreatePlatformAdministratorRequest(existingEmail.ToUpperInvariant(), PlatformAdministratorRole.SupportStaff),
            currentUser,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Creates_Administrator_And_Publishes_Audit_Event_On_Happy_Path()
    {
        var ownerEmail = $"owner-{Guid.NewGuid():N}@test.com";
        await SeedOwnerAsync(ownerEmail);

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher);
        var actorId = Guid.NewGuid();
        var currentUser = new FakeCurrentUser(actorId, ownerEmail);

        var newEmail = $"NEW-Admin-{Guid.NewGuid():N}@Test.com";

        var result = await handler.HandleAsync(
            new CreatePlatformAdministratorRequest(newEmail, PlatformAdministratorRole.SupportStaff),
            currentUser,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(newEmail.Trim().ToLowerInvariant(), result.Value.Email);
        Assert.Equal(PlatformAdministratorRole.SupportStaff, result.Value.Role);
        Assert.True(result.Value.IsEnabled);

        await using var db2 = fixture.BuildContext();
        var reloaded = await db2.PlatformAdministrators.FirstAsync(a => a.Id == result.Value.Id);
        Assert.Equal(newEmail.Trim().ToLowerInvariant(), reloaded.Email);

        Assert.Single(auditPublisher.PublishedEvents, e => e is PlatformAdministratorCreatedAuditEvent);
    }
}
