using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.CreatePlatformAdministrator;
using HR.Modules.Identity.Features.RetryPlatformAdministratorProvisioning;
using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class RetryPlatformAdministratorProvisioningHandlerTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly FakeClock Clock = new(Now.UtcDateTime);
    private static readonly IConfiguration Configuration = new ConfigurationBuilder().Build();

    private RetryPlatformAdministratorProvisioningHandler BuildHandler(
        IdentityDbContext db, FakeAuditEventPublisher auditPublisher, FakeSupabaseAuthGateway? gateway = null)
    {
        var provisioningDelivery = new CreatePlatformAdministratorHandler(
            db, gateway ?? new FakeSupabaseAuthGateway(), Clock, Configuration, auditPublisher,
            NullLogger<CreatePlatformAdministratorHandler>.Instance);

        return new RetryPlatformAdministratorProvisioningHandler(db, provisioningDelivery, Configuration, Clock, auditPublisher);
    }

    private async Task SeedOwnerAsync(string email, bool isEnabled = true)
    {
        await using var db = fixture.BuildContext();
        var owner = PlatformAdministrator.Create(email, PlatformAdministratorRole.PlatformOwner, Now);
        if (!isEnabled)
            owner.Disable(Now, actorUserId: null);
        db.PlatformAdministrators.Add(owner);
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedFailedAdministratorAsync(
        string email, bool isNewIdentityProviderAccount, bool isEnabled = true, bool withCorrelationId = true)
    {
        await using var db = fixture.BuildContext();
        var admin = PlatformAdministrator.Create(email, PlatformAdministratorRole.SupportStaff, Now);
        if (withCorrelationId)
        {
            admin.BeginProvisioning(isNewIdentityProviderAccount, Guid.NewGuid(), Now);
            admin.MarkProvisioningFailed("simulated_failure", Now);
        }
        if (!isEnabled)
            admin.Disable(Now, actorUserId: null);
        db.PlatformAdministrators.Add(admin);
        await db.SaveChangesAsync();
        return admin.Id;
    }

    [Fact]
    public async Task HandleAsync_Returns_Unauthorized_When_Caller_Is_Not_A_PlatformOwner()
    {
        await using var db = fixture.BuildContext();
        var handler = BuildHandler(db, new FakeAuditEventPublisher());
        var currentUser = new FakeCurrentUser(Guid.NewGuid(), "not-an-owner@test.com");

        var result = await handler.HandleAsync(
            new RetryPlatformAdministratorProvisioningRequest(Guid.NewGuid()), currentUser, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unauthorized", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Administrator_Missing()
    {
        var ownerEmail = $"owner-{Guid.NewGuid():N}@test.com";
        await SeedOwnerAsync(ownerEmail);

        await using var db = fixture.BuildContext();
        var handler = BuildHandler(db, new FakeAuditEventPublisher());
        var currentUser = new FakeCurrentUser(Guid.NewGuid(), ownerEmail);

        var result = await handler.HandleAsync(
            new RetryPlatformAdministratorProvisioningRequest(Guid.NewGuid()), currentUser, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Already_Active()
    {
        var ownerEmail = $"owner-{Guid.NewGuid():N}@test.com";
        await SeedOwnerAsync(ownerEmail);

        Guid activeId;
        await using (var seedDb = fixture.BuildContext())
        {
            var admin = PlatformAdministrator.Create($"active-{Guid.NewGuid():N}@test.com", PlatformAdministratorRole.SupportStaff, Now);
            seedDb.PlatformAdministrators.Add(admin);
            await seedDb.SaveChangesAsync();
            activeId = admin.Id;
        }

        await using var db = fixture.BuildContext();
        var handler = BuildHandler(db, new FakeAuditEventPublisher());
        var currentUser = new FakeCurrentUser(Guid.NewGuid(), ownerEmail);

        var result = await handler.HandleAsync(
            new RetryPlatformAdministratorProvisioningRequest(activeId), currentUser, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Administrator_Is_Disabled()
    {
        var ownerEmail = $"owner-{Guid.NewGuid():N}@test.com";
        await SeedOwnerAsync(ownerEmail);

        var targetId = await SeedFailedAdministratorAsync(
            $"disabled-{Guid.NewGuid():N}@test.com", isNewIdentityProviderAccount: true, isEnabled: false);

        await using var db = fixture.BuildContext();
        var handler = BuildHandler(db, new FakeAuditEventPublisher());
        var currentUser = new FakeCurrentUser(Guid.NewGuid(), ownerEmail);

        var result = await handler.HandleAsync(
            new RetryPlatformAdministratorProvisioningRequest(targetId), currentUser, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    /// <summary>
    /// A row that predates the provisioning workflow (null ProvisioningCorrelationId) — reachable
    /// via PlatformAdministrator.Create alone (never calling BeginProvisioning), which leaves
    /// ProvisioningStatus defaulted to Active per the domain's own remarks. To exercise this branch
    /// (a Pending*/Failed status but a null correlation id) would require a state combination the
    /// public domain API cannot produce — BeginProvisioning always sets the correlation id together
    /// with the Pending* status, and there is no way to reach Failed without going through
    /// BeginProvisioning first. This scenario is therefore not reachable through the real domain API
    /// and is intentionally skipped rather than forced via reflection.
    /// </summary>
    [Fact(Skip = "Not reachable via the public PlatformAdministrator API: BeginProvisioning always sets ProvisioningCorrelationId together with a Pending* status, and Failed can only be reached via BeginProvisioning first — so a Pending/Failed row with a null correlation id cannot be constructed without reflection.")]
    public Task HandleAsync_Returns_Conflict_When_Correlation_Id_Is_Null()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public async Task HandleAsync_Failed_New_Account_Path_Retries_CreatePendingUserWithMetadata_And_Moves_To_PendingProvisioning()
    {
        var ownerEmail = $"owner-{Guid.NewGuid():N}@test.com";
        await SeedOwnerAsync(ownerEmail);

        var targetEmail = $"retry-new-{Guid.NewGuid():N}@test.com";
        var targetId = await SeedFailedAdministratorAsync(targetEmail, isNewIdentityProviderAccount: true);

        var gateway = new FakeSupabaseAuthGateway();
        var auditPublisher = new FakeAuditEventPublisher();
        await using var db = fixture.BuildContext();
        var handler = BuildHandler(db, auditPublisher, gateway);
        var currentUser = new FakeCurrentUser(Guid.NewGuid(), ownerEmail);

        var result = await handler.HandleAsync(
            new RetryPlatformAdministratorProvisioningRequest(targetId), currentUser, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PlatformAdministratorProvisioningStatus.PendingProvisioning, result.Value.ProvisioningStatus);

        Assert.Contains(gateway.PendingUsersCreatedWithMetadata, u => u.Email == targetEmail.Trim().ToLowerInvariant());
        Assert.Empty(gateway.PasswordResetRequests);

        Assert.Single(auditPublisher.PublishedEvents, e => e is PlatformAdministratorProvisioningRetriedAuditEvent);

        await using var verifyDb = fixture.BuildContext();
        var reloaded = await verifyDb.PlatformAdministrators.FirstAsync(a => a.Id == targetId);
        Assert.Equal(PlatformAdministratorProvisioningStatus.PendingProvisioning, reloaded.ProvisioningStatus);
    }

    [Fact]
    public async Task HandleAsync_Failed_Link_Verification_Path_Retries_RequestPasswordReset_And_Moves_To_PendingLinkVerification()
    {
        var ownerEmail = $"owner-{Guid.NewGuid():N}@test.com";
        await SeedOwnerAsync(ownerEmail);

        var targetEmail = $"retry-link-{Guid.NewGuid():N}@test.com";
        var targetId = await SeedFailedAdministratorAsync(targetEmail, isNewIdentityProviderAccount: false);

        var gateway = new FakeSupabaseAuthGateway();
        var auditPublisher = new FakeAuditEventPublisher();
        await using var db = fixture.BuildContext();
        var handler = BuildHandler(db, auditPublisher, gateway);
        var currentUser = new FakeCurrentUser(Guid.NewGuid(), ownerEmail);

        var result = await handler.HandleAsync(
            new RetryPlatformAdministratorProvisioningRequest(targetId), currentUser, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PlatformAdministratorProvisioningStatus.PendingLinkVerification, result.Value.ProvisioningStatus);

        Assert.Contains(gateway.PasswordResetRequests, r => r.Email == targetEmail.Trim().ToLowerInvariant());
        Assert.Empty(gateway.PendingUsersCreatedWithMetadata);
    }

    [Fact]
    public async Task HandleAsync_Returns_Success_With_Failed_Status_When_Retry_Delivery_Throws_Again()
    {
        var ownerEmail = $"owner-{Guid.NewGuid():N}@test.com";
        await SeedOwnerAsync(ownerEmail);

        var targetEmail = $"retry-fails-again-{Guid.NewGuid():N}@test.com";
        var targetId = await SeedFailedAdministratorAsync(targetEmail, isNewIdentityProviderAccount: true);

        var gateway = new FakeSupabaseAuthGateway { ShouldThrowOnCreate = true };
        var auditPublisher = new FakeAuditEventPublisher();
        await using var db = fixture.BuildContext();
        var handler = BuildHandler(db, auditPublisher, gateway);
        var currentUser = new FakeCurrentUser(Guid.NewGuid(), ownerEmail);

        var result = await handler.HandleAsync(
            new RetryPlatformAdministratorProvisioningRequest(targetId), currentUser, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PlatformAdministratorProvisioningStatus.Failed, result.Value.ProvisioningStatus);

        Assert.Contains(auditPublisher.PublishedEvents, e => e is PlatformAdministratorProvisioningRetriedAuditEvent);
        Assert.Contains(auditPublisher.PublishedEvents, e => e is PlatformAdministratorProvisioningFailedAuditEvent);

        await using var verifyDb = fixture.BuildContext();
        var reloaded = await verifyDb.PlatformAdministrators.FirstAsync(a => a.Id == targetId);
        Assert.Equal(PlatformAdministratorProvisioningStatus.Failed, reloaded.ProvisioningStatus);
    }
}
