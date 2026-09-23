using HR.Modules.Identity.Services.AccountEmailPolicy;
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
            TestAccountCreationEmailGuard.Create(auditPublisher, Clock),
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

    // ── Ticket 9: work-email policy ─────────────────────────────────────────────

    [Theory]
    [InlineData("gmail.com")]
    [InlineData("OUTLOOK.COM")]
    [InlineData("proton.me")]
    public async Task HandleAsync_Rejects_Public_Email_Domain_Before_Any_Row_Or_Provider_Call(string domain)
    {
        var ownerEmail = $"owner-{Guid.NewGuid():N}@test.com";
        await SeedOwnerAsync(ownerEmail);

        var auditPublisher = new FakeAuditEventPublisher();
        // If the provider lookup were reached it would throw and surface as "unexpected" — so a
        // work_email_required result proves the policy ran before any provider interaction.
        var gateway = new FakeSupabaseAuthGateway { ShouldThrowOnGetUserIdByEmail = true };
        var handler = BuildHandler(auditPublisher, gateway);
        var actorId = Guid.NewGuid();
        var newEmail = $"new-admin-{Guid.NewGuid():N}@{domain}";

        var result = await handler.HandleAsync(
            new CreatePlatformAdministratorRequest(newEmail, PlatformAdministratorRole.SupportStaff),
            new FakeCurrentUser(actorId, ownerEmail),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AccountCreationEmailGuard.WorkEmailRequiredCode, result.Error.Code);
        Assert.Equal(AccountCreationEmailGuard.WorkEmailRequiredMessage, result.Error.Message);

        await using var db = fixture.BuildContext();
        Assert.False(await db.PlatformAdministrators.AnyAsync(a => a.Email == newEmail.Trim().ToLowerInvariant()));

        Assert.Empty(gateway.CreatedUsers);
        Assert.Empty(gateway.ConfirmedUsersCreated);
        Assert.Empty(gateway.PendingUsersCreatedWithMetadata);
        Assert.Empty(gateway.PasswordResetRequests);
        Assert.Empty(gateway.RecoveryLinksGenerated);

        var rejection = Assert.IsType<AccountCreationEmailRejectedAuditEvent>(Assert.Single(auditPublisher.PublishedEvents));
        Assert.Equal("platform-administrator", rejection.Path);
        Assert.Equal(actorId, rejection.ActorUserId);
        Assert.Equal(HR.SharedKernel.AuditActorType.Human, rejection.ActorKind);
    }

    [Fact]
    public async Task HandleAsync_Public_Email_Matching_An_Existing_Administrator_Returns_WorkEmailRequired_Not_Conflict()
    {
        // The policy check precedes the existing-administrator lookup, so the response never
        // reveals whether an administrator already exists for a public address.
        var ownerEmail = $"owner-{Guid.NewGuid():N}@test.com";
        await SeedOwnerAsync(ownerEmail);

        var existingPublicEmail = $"legacy-admin-{Guid.NewGuid():N}@gmail.com";
        await using (var seed = fixture.BuildContext())
        {
            seed.PlatformAdministrators.Add(
                PlatformAdministrator.Create(existingPublicEmail, PlatformAdministratorRole.SupportStaff, Now));
            await seed.SaveChangesAsync();
        }

        var result = await BuildHandler(new FakeAuditEventPublisher()).HandleAsync(
            new CreatePlatformAdministratorRequest(existingPublicEmail, PlatformAdministratorRole.SupportStaff),
            new FakeCurrentUser(Guid.NewGuid(), ownerEmail),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AccountCreationEmailGuard.WorkEmailRequiredCode, result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Unauthorized_Caller_Is_Rejected_Before_The_Email_Policy_Runs()
    {
        var auditPublisher = new FakeAuditEventPublisher();

        var result = await BuildHandler(auditPublisher).HandleAsync(
            new CreatePlatformAdministratorRequest("new-admin@gmail.com", PlatformAdministratorRole.SupportStaff),
            new FakeCurrentUser(Guid.NewGuid(), $"not-an-owner-{Guid.NewGuid():N}@test.com"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unauthorized", result.Error.Code);
        Assert.Empty(auditPublisher.PublishedEvents);
    }

    [Fact]
    public async Task HandleAsync_Existing_Owner_On_A_Public_Domain_Can_Still_Create_An_Org_Domain_Administrator()
    {
        // Existing accounts on public domains are unaffected by the policy — only the NEW
        // administrator's address is evaluated.
        var ownerEmail = $"owner-{Guid.NewGuid():N}@gmail.com";
        await SeedOwnerAsync(ownerEmail);

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher);
        var newEmail = $"new-admin-{Guid.NewGuid():N}@acme.example";

        var result = await handler.HandleAsync(
            new CreatePlatformAdministratorRequest(newEmail, PlatformAdministratorRole.SupportStaff),
            new FakeCurrentUser(Guid.NewGuid(), ownerEmail),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(newEmail, result.Value.Email);

        await using var db = fixture.BuildContext();
        Assert.True(await db.PlatformAdministrators.AnyAsync(a => a.Email == newEmail));
        Assert.DoesNotContain(auditPublisher.PublishedEvents, e => e is AccountCreationEmailRejectedAuditEvent);
    }
}
