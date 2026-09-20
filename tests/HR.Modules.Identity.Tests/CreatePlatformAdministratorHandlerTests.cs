using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.CreatePlatformAdministrator;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class CreatePlatformAdministratorHandlerTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly FakeClock Clock = new(Now.UtcDateTime);
    private static readonly IConfiguration Configuration = new ConfigurationBuilder().Build();

    private CreatePlatformAdministratorHandler BuildHandler(FakeAuditEventPublisher auditPublisher, FakeSupabaseAuthGateway? gateway = null) =>
        new(fixture.BuildContext(), gateway ?? new FakeSupabaseAuthGateway(), Clock, Configuration, auditPublisher,
            NullLogger<CreatePlatformAdministratorHandler>.Instance);

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

    [Fact]
    public async Task HandleAsync_New_Provider_Account_Path_Begins_PendingProvisioning_And_Creates_Pending_User()
    {
        var ownerEmail = $"owner-{Guid.NewGuid():N}@test.com";
        await SeedOwnerAsync(ownerEmail);

        var gateway = new FakeSupabaseAuthGateway();
        var handler = BuildHandler(new FakeAuditEventPublisher(), gateway);
        var currentUser = new FakeCurrentUser(Guid.NewGuid(), ownerEmail);

        var newEmail = $"new-provider-{Guid.NewGuid():N}@test.com";

        var result = await handler.HandleAsync(
            new CreatePlatformAdministratorRequest(newEmail, PlatformAdministratorRole.SupportStaff),
            currentUser,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PlatformAdministratorProvisioningStatus.PendingProvisioning, result.Value.ProvisioningStatus);

        await using var db = fixture.BuildContext();
        var reloaded = await db.PlatformAdministrators.FirstAsync(a => a.Id == result.Value.Id);
        Assert.Equal(PlatformAdministratorProvisioningStatus.PendingProvisioning, reloaded.ProvisioningStatus);
        Assert.NotNull(reloaded.ProvisioningCorrelationId);
        Assert.True(reloaded.IsNewIdentityProviderAccount);

        var pendingUser = Assert.Single(gateway.PendingUsersCreatedWithMetadata, u => u.Email == newEmail.Trim().ToLowerInvariant());
        Assert.Equal(reloaded.ProvisioningCorrelationId!.Value.ToString(), pendingUser.Metadata["platform_admin_provisioning_id"]);
        Assert.Empty(gateway.PasswordResetRequests);
    }

    [Fact]
    public async Task HandleAsync_Existing_Provider_Account_Path_Begins_PendingLinkVerification_And_Requests_Password_Reset()
    {
        var ownerEmail = $"owner-{Guid.NewGuid():N}@test.com";
        await SeedOwnerAsync(ownerEmail);

        var gateway = new FakeSupabaseAuthGateway();
        var newEmail = $"existing-provider-{Guid.NewGuid():N}@test.com";
        gateway.UserIdsByEmail[newEmail.Trim().ToLowerInvariant()] = Guid.NewGuid();

        var handler = BuildHandler(new FakeAuditEventPublisher(), gateway);
        var currentUser = new FakeCurrentUser(Guid.NewGuid(), ownerEmail);

        var result = await handler.HandleAsync(
            new CreatePlatformAdministratorRequest(newEmail, PlatformAdministratorRole.SupportStaff),
            currentUser,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PlatformAdministratorProvisioningStatus.PendingLinkVerification, result.Value.ProvisioningStatus);

        await using var db = fixture.BuildContext();
        var reloaded = await db.PlatformAdministrators.FirstAsync(a => a.Id == result.Value.Id);
        Assert.Equal(PlatformAdministratorProvisioningStatus.PendingLinkVerification, reloaded.ProvisioningStatus);
        Assert.False(reloaded.IsNewIdentityProviderAccount);

        Assert.Contains(gateway.PasswordResetRequests, r => r.Email == newEmail.Trim().ToLowerInvariant());
        Assert.Empty(gateway.PendingUsersCreatedWithMetadata);
    }

    [Fact]
    public async Task HandleAsync_Returns_Failure_And_Persists_Nothing_When_Provider_Lookup_Throws()
    {
        var ownerEmail = $"owner-{Guid.NewGuid():N}@test.com";
        await SeedOwnerAsync(ownerEmail);

        var gateway = new FakeSupabaseAuthGateway { ShouldThrowOnGetUserIdByEmail = true };
        var handler = BuildHandler(new FakeAuditEventPublisher(), gateway);
        var currentUser = new FakeCurrentUser(Guid.NewGuid(), ownerEmail);

        var newEmail = $"lookup-fails-{Guid.NewGuid():N}@test.com";

        var result = await handler.HandleAsync(
            new CreatePlatformAdministratorRequest(newEmail, PlatformAdministratorRole.SupportStaff),
            currentUser,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unexpected", result.Error.Code);

        await using var db = fixture.BuildContext();
        var exists = await db.PlatformAdministrators.AnyAsync(a => a.Email == newEmail.Trim().ToLowerInvariant());
        Assert.False(exists);
    }

    [Fact]
    public async Task HandleAsync_Returns_Success_With_Failed_Status_When_Provisioning_Delivery_Throws()
    {
        var ownerEmail = $"owner-{Guid.NewGuid():N}@test.com";
        await SeedOwnerAsync(ownerEmail);

        var gateway = new FakeSupabaseAuthGateway { ShouldThrowOnCreate = true };
        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher, gateway);
        var currentUser = new FakeCurrentUser(Guid.NewGuid(), ownerEmail);

        var newEmail = $"delivery-fails-{Guid.NewGuid():N}@test.com";

        var result = await handler.HandleAsync(
            new CreatePlatformAdministratorRequest(newEmail, PlatformAdministratorRole.SupportStaff),
            currentUser,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PlatformAdministratorProvisioningStatus.Failed, result.Value.ProvisioningStatus);

        await using var db = fixture.BuildContext();
        var reloaded = await db.PlatformAdministrators.FirstAsync(a => a.Id == result.Value.Id);
        Assert.Equal(PlatformAdministratorProvisioningStatus.Failed, reloaded.ProvisioningStatus);
        Assert.False(string.IsNullOrWhiteSpace(reloaded.ProvisioningFailureReason));

        Assert.Contains(auditPublisher.PublishedEvents, e => e is PlatformAdministratorCreatedAuditEvent);
        Assert.Contains(auditPublisher.PublishedEvents, e => e is PlatformAdministratorProvisioningFailedAuditEvent);
    }

    /// <summary>
    /// Concurrent creation for the same normalized email: two independent handler instances, each
    /// with their own IdentityDbContext against the SAME real Postgres Testcontainer, both attempt
    /// to create an administrator for the identical email essentially simultaneously — both can pass
    /// the pre-check AnyAsync race, but only one INSERT can win at the DB level via the unique index
    /// on email. This MUST use real Postgres (not EF InMemory), which does not enforce unique
    /// constraints the same way.
    /// </summary>
    [Fact]
    public async Task HandleAsync_Only_One_Of_Two_Concurrent_Creation_Attempts_For_Same_Email_Succeeds()
    {
        var ownerEmail = $"owner-{Guid.NewGuid():N}@test.com";
        await SeedOwnerAsync(ownerEmail);

        var sharedEmail = $"race-{Guid.NewGuid():N}@test.com";

        var handler1 = BuildHandler(new FakeAuditEventPublisher());
        var handler2 = BuildHandler(new FakeAuditEventPublisher());

        var currentUser1 = new FakeCurrentUser(Guid.NewGuid(), ownerEmail);
        var currentUser2 = new FakeCurrentUser(Guid.NewGuid(), ownerEmail);

        var task1 = handler1.HandleAsync(
            new CreatePlatformAdministratorRequest(sharedEmail, PlatformAdministratorRole.SupportStaff),
            currentUser1,
            CancellationToken.None);
        var task2 = handler2.HandleAsync(
            new CreatePlatformAdministratorRequest(sharedEmail, PlatformAdministratorRole.SupportStaff),
            currentUser2,
            CancellationToken.None);

        var results = await Task.WhenAll(task1, task2);

        Assert.Single(results, r => r.IsSuccess);
        Assert.Single(results, r => r.IsFailure && r.Error.Code == "conflict");

        await using var db = fixture.BuildContext();
        var count = await db.PlatformAdministrators.CountAsync(a => a.Email == sharedEmail.Trim().ToLowerInvariant());
        Assert.Equal(1, count);
    }
}
