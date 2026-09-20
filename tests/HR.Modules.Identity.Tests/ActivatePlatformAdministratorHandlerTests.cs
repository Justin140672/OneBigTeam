using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.ActivatePlatformAdministrator;
using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class ActivatePlatformAdministratorHandlerTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly FakeClock Clock = new(Now.UtcDateTime);

    private ActivatePlatformAdministratorHandler BuildHandler(FakeAuditEventPublisher auditPublisher, out IdentityDbContext db)
    {
        db = fixture.BuildContext();
        return new ActivatePlatformAdministratorHandler(db, Clock, auditPublisher);
    }

    private async Task<PlatformAdministrator> SeedAdministratorAsync(
        string email, string provisioningStatus, bool isNewIdentityProviderAccount, bool isEnabled = true)
    {
        await using var db = fixture.BuildContext();
        var admin = PlatformAdministrator.Create(email, PlatformAdministratorRole.SupportStaff, Now);
        admin.BeginProvisioning(isNewIdentityProviderAccount, Guid.NewGuid(), Now);
        if (!isEnabled)
            admin.Disable(Now, actorUserId: null);
        db.PlatformAdministrators.Add(admin);
        await db.SaveChangesAsync();
        return admin;
    }

    [Fact]
    public async Task HandleAsync_New_Account_Path_Activates_And_Publishes_Audit_Event()
    {
        var email = $"new-account-{Guid.NewGuid():N}@test.com";
        var seeded = await SeedAdministratorAsync(email, PlatformAdministratorProvisioningStatus.PendingProvisioning, isNewIdentityProviderAccount: true);

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher, out var db);
        var supabaseUserId = Guid.NewGuid();
        var currentUser = new FakeCurrentUser(supabaseUserId, email);

        var result = await handler.HandleAsync(currentUser, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(seeded.Id, result.Value.Id);

        await using var verifyDb = fixture.BuildContext();
        var reloaded = await verifyDb.PlatformAdministrators.FirstAsync(a => a.Id == seeded.Id);
        Assert.Equal(PlatformAdministratorProvisioningStatus.Active, reloaded.ProvisioningStatus);
        Assert.Equal(supabaseUserId, reloaded.SupabaseAuthUserId);
        Assert.Equal(2, reloaded.Version);

        Assert.Single(auditPublisher.PublishedEvents, e => e is PlatformAdministratorActivatedAuditEvent);

        await db.DisposeAsync();
    }

    [Fact]
    public async Task HandleAsync_Existing_Account_Path_Activates_And_Publishes_Audit_Event()
    {
        var email = $"existing-account-{Guid.NewGuid():N}@test.com";
        var seeded = await SeedAdministratorAsync(email, PlatformAdministratorProvisioningStatus.PendingLinkVerification, isNewIdentityProviderAccount: false);

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher, out var db);
        var supabaseUserId = Guid.NewGuid();
        var currentUser = new FakeCurrentUser(supabaseUserId, email);

        var result = await handler.HandleAsync(currentUser, CancellationToken.None);

        Assert.True(result.IsSuccess);

        await using var verifyDb = fixture.BuildContext();
        var reloaded = await verifyDb.PlatformAdministrators.FirstAsync(a => a.Id == seeded.Id);
        Assert.Equal(PlatformAdministratorProvisioningStatus.Active, reloaded.ProvisioningStatus);
        Assert.Equal(supabaseUserId, reloaded.SupabaseAuthUserId);

        Assert.Single(auditPublisher.PublishedEvents, e => e is PlatformAdministratorActivatedAuditEvent);

        await db.DisposeAsync();
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_No_Row_Matches_Caller_Email()
    {
        var handler = BuildHandler(new FakeAuditEventPublisher(), out var db);
        var currentUser = new FakeCurrentUser(Guid.NewGuid(), $"no-such-admin-{Guid.NewGuid():N}@test.com");

        var result = await handler.HandleAsync(currentUser, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);

        await db.DisposeAsync();
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Already_Active()
    {
        var email = $"already-active-{Guid.NewGuid():N}@test.com";
        await using (var db = fixture.BuildContext())
        {
            var admin = PlatformAdministrator.Create(email, PlatformAdministratorRole.SupportStaff, Now);
            db.PlatformAdministrators.Add(admin);
            await db.SaveChangesAsync();
        }

        var handler = BuildHandler(new FakeAuditEventPublisher(), out var handlerDb);
        var currentUser = new FakeCurrentUser(Guid.NewGuid(), email);

        var result = await handler.HandleAsync(currentUser, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);

        await handlerDb.DisposeAsync();
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Administrator_Is_Disabled()
    {
        var email = $"disabled-{Guid.NewGuid():N}@test.com";
        await SeedAdministratorAsync(email, PlatformAdministratorProvisioningStatus.PendingProvisioning, isNewIdentityProviderAccount: true, isEnabled: false);

        var handler = BuildHandler(new FakeAuditEventPublisher(), out var db);
        var currentUser = new FakeCurrentUser(Guid.NewGuid(), email);

        var result = await handler.HandleAsync(currentUser, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);

        await db.DisposeAsync();
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_SupabaseAuthUserId_Already_Linked_To_A_Different_Row()
    {
        var linkedSupabaseId = Guid.NewGuid();
        var otherEmail = $"other-admin-{Guid.NewGuid():N}@test.com";
        await using (var db = fixture.BuildContext())
        {
            var other = PlatformAdministrator.Create(
                otherEmail, PlatformAdministratorRole.SupportStaff, Now, supabaseAuthUserId: linkedSupabaseId);
            db.PlatformAdministrators.Add(other);
            await db.SaveChangesAsync();
        }

        var email = $"pending-{Guid.NewGuid():N}@test.com";
        await SeedAdministratorAsync(email, PlatformAdministratorProvisioningStatus.PendingProvisioning, isNewIdentityProviderAccount: true);

        var handler = BuildHandler(new FakeAuditEventPublisher(), out var handlerDb);
        var currentUser = new FakeCurrentUser(linkedSupabaseId, email);

        var result = await handler.HandleAsync(currentUser, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);

        await handlerDb.DisposeAsync();
    }

    [Fact]
    public async Task HandleAsync_Returns_Unauthorized_When_Caller_UserId_Is_Null()
    {
        var handler = BuildHandler(new FakeAuditEventPublisher(), out var db);
        var currentUser = new FakeCurrentUser(null, "someone@test.com");

        var result = await handler.HandleAsync(currentUser, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unauthorized", result.Error.Code);

        await db.DisposeAsync();
    }

    [Fact]
    public async Task HandleAsync_Returns_Unauthorized_When_Caller_Email_Is_Empty()
    {
        var handler = BuildHandler(new FakeAuditEventPublisher(), out var db);
        var currentUser = new FakeCurrentUser(Guid.NewGuid(), email: "");

        var result = await handler.HandleAsync(currentUser, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unauthorized", result.Error.Code);

        await db.DisposeAsync();
    }

    /// <summary>
    /// Concurrent/replayed activation for the SAME row: two contexts each load the still-Pending
    /// row before either handler saves (mirrors RedeemSupportSessionHandlerTests's concurrency
    /// regression pattern), then both attempt CompleteProvisioning + SaveChangesWithConcurrencyAsync.
    /// Only the first save wins; the second's pinned Version OriginalValue no longer matches, so EF
    /// raises DbUpdateConcurrencyException, translated to Error.Concurrency.
    /// </summary>
    [Fact]
    public async Task HandleAsync_Only_One_Of_Two_Concurrent_Activation_Attempts_For_Same_Row_Succeeds()
    {
        var email = $"concurrent-activate-{Guid.NewGuid():N}@test.com";
        Guid administratorId;
        await using (var seedDb = fixture.BuildContext())
        {
            var admin = PlatformAdministrator.Create(email, PlatformAdministratorRole.SupportStaff, Now);
            admin.BeginProvisioning(isNewIdentityProviderAccount: true, Guid.NewGuid(), Now);
            seedDb.PlatformAdministrators.Add(admin);
            await seedDb.SaveChangesAsync();
            administratorId = admin.Id;
        }

        await using var db1 = fixture.BuildContext();
        await using var db2 = fixture.BuildContext();

        // Force each context to independently load (and track) the still-pending row before either
        // handler writes anything, so this is a genuine race rather than sequential reads.
        await db1.PlatformAdministrators.SingleAsync(a => a.Id == administratorId);
        await db2.PlatformAdministrators.SingleAsync(a => a.Id == administratorId);

        var publisher1 = new FakeAuditEventPublisher();
        var publisher2 = new FakeAuditEventPublisher();
        var handler1 = new ActivatePlatformAdministratorHandler(db1, Clock, publisher1);
        var handler2 = new ActivatePlatformAdministratorHandler(db2, Clock, publisher2);

        var supabaseUserId1 = Guid.NewGuid();
        var supabaseUserId2 = Guid.NewGuid();
        var currentUser1 = new FakeCurrentUser(supabaseUserId1, email);
        var currentUser2 = new FakeCurrentUser(supabaseUserId2, email);

        var result1 = await handler1.HandleAsync(currentUser1, CancellationToken.None);
        var result2 = await handler2.HandleAsync(currentUser2, CancellationToken.None);

        Assert.True(result1.IsSuccess);
        Assert.True(result2.IsFailure);
        Assert.Equal("concurrency", result2.Error.Code);

        Assert.Single(publisher1.PublishedEvents);
        Assert.Empty(publisher2.PublishedEvents);

        await using var verifyDb = fixture.BuildContext();
        var reloaded = await verifyDb.PlatformAdministrators.SingleAsync(a => a.Id == administratorId);
        Assert.Equal(supabaseUserId1, reloaded.SupabaseAuthUserId);
        Assert.Equal(2, reloaded.Version);
    }
}
