using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.ResetPlatformAdministratorMfa;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class ResetPlatformAdministratorMfaHandlerTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly FakeClock Clock = new(Now.UtcDateTime);

    private const string ProviderRejectedMessage =
        "The identity provider rejected the MFA reset. No changes were made. Please retry, and contact support if it keeps failing.";

    private ResetPlatformAdministratorMfaHandler BuildHandler(
        FakeSupabaseAuthGateway gateway, FakeEmailSender emailSender, FakeAuditEventPublisher auditPublisher) =>
        new(fixture.BuildContext(), gateway, emailSender, Clock, auditPublisher,
            NullLogger<ResetPlatformAdministratorMfaHandler>.Instance);

    private static ResetPlatformAdministratorMfaRequest Request(Guid id, string reason = "compromised device") =>
        new(id, Confirmed: true, Reason: reason);

    private async Task<string> SeedOwnerAsync(bool isEnabled = true)
    {
        var email = $"owner-{Guid.NewGuid():N}@test.com";
        await using var db = fixture.BuildContext();
        var owner = PlatformAdministrator.Create(
            email, PlatformAdministratorRole.PlatformOwner, Now, createdByUserId: null,
            supabaseAuthUserId: Guid.NewGuid());
        if (!isEnabled)
            owner.Disable(Now, actorUserId: null);
        db.PlatformAdministrators.Add(owner);
        await db.SaveChangesAsync();
        return email;
    }

    private async Task<(Guid Id, string Email, Guid SupabaseId)> SeedTargetAsync(
        PlatformAdministratorRole role = PlatformAdministratorRole.SupportStaff,
        bool isEnabled = true,
        Guid? supabaseAuthUserId = null)
    {
        var supabaseId = supabaseAuthUserId ?? Guid.NewGuid();
        await using var db = fixture.BuildContext();
        var target = PlatformAdministrator.Create(
            $"target-{Guid.NewGuid():N}@test.com", role, Now, createdByUserId: null,
            supabaseAuthUserId: supabaseAuthUserId);
        if (!isEnabled)
            target.Disable(Now, actorUserId: null);
        db.PlatformAdministrators.Add(target);
        await db.SaveChangesAsync();
        return (target.Id, target.Email, supabaseId);
    }

    [Fact]
    public async Task HandleAsync_Returns_Unauthorized_When_Caller_Is_Not_A_PlatformOwner()
    {
        var (targetId, _, _) = await SeedTargetAsync(supabaseAuthUserId: Guid.NewGuid());
        var gateway = new FakeSupabaseAuthGateway();
        var handler = BuildHandler(gateway, new FakeEmailSender(), new FakeAuditEventPublisher());
        var currentUser = new FakeCurrentUser(Guid.NewGuid(), "not-an-owner@test.com");

        var result = await handler.HandleAsync(Request(targetId), currentUser, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unauthorized", result.Error.Code);
        Assert.Empty(gateway.MfaFactorRemovals);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Administrator_Missing()
    {
        var ownerEmail = await SeedOwnerAsync();
        var handler = BuildHandler(new FakeSupabaseAuthGateway(), new FakeEmailSender(), new FakeAuditEventPublisher());
        var currentUser = new FakeCurrentUser(Guid.NewGuid(), ownerEmail);

        var result = await handler.HandleAsync(Request(Guid.NewGuid()), currentUser, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Target_Is_Disabled()
    {
        var ownerEmail = await SeedOwnerAsync();
        var (targetId, _, _) = await SeedTargetAsync(isEnabled: false, supabaseAuthUserId: Guid.NewGuid());
        var gateway = new FakeSupabaseAuthGateway();
        var handler = BuildHandler(gateway, new FakeEmailSender(), new FakeAuditEventPublisher());
        var currentUser = new FakeCurrentUser(Guid.NewGuid(), ownerEmail);

        var result = await handler.HandleAsync(Request(targetId), currentUser, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Empty(gateway.MfaFactorRemovals);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Target_Is_The_Last_Enabled_PlatformOwner()
    {
        var ownerEmail = await SeedOwnerAsync();
        // The only enabled owner is the caller; target owner has no other enabled owner besides itself.
        var (targetId, _, _) = await SeedTargetAsync(
            role: PlatformAdministratorRole.PlatformOwner, supabaseAuthUserId: Guid.NewGuid());

        // Re-seed so the caller is the target (last enabled owner resetting itself).
        var gateway = new FakeSupabaseAuthGateway();
        var handler = BuildHandler(gateway, new FakeEmailSender(), new FakeAuditEventPublisher());

        string targetEmail;
        await using (var db = fixture.BuildContext())
        {
            targetEmail = (await db.PlatformAdministrators.FindAsync(targetId))!.Email;
        }

        // Disable the other owner so `targetId` is genuinely the last enabled owner.
        await using (var db = fixture.BuildContext())
        {
            // Disable every other enabled PlatformOwner (the shared fixture DB may carry rows seeded
            // by sibling tests) so `targetId` is genuinely the last enabled owner.
            var others = db.PlatformAdministrators
                .Where(a => a.Id != targetId && a.IsEnabled && a.Role == PlatformAdministratorRole.PlatformOwner)
                .ToList();
            foreach (var other in others)
                other.Disable(Now, actorUserId: null);
            db.SaveChanges();
        }

        var currentUser = new FakeCurrentUser(Guid.NewGuid(), targetEmail);
        var result = await handler.HandleAsync(Request(targetId), currentUser, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Equal("Cannot reset MFA for the last enabled platform owner.", result.Error.Message);
        Assert.Empty(gateway.MfaFactorRemovals);
    }

    [Fact]
    public async Task HandleAsync_Succeeds_When_Target_Is_PlatformOwner_But_Another_Enabled_Owner_Exists()
    {
        var ownerEmail = await SeedOwnerAsync();
        var (targetId, targetEmail, supabaseId) = await SeedTargetAsync(
            role: PlatformAdministratorRole.PlatformOwner, supabaseAuthUserId: Guid.NewGuid());
        var gateway = new FakeSupabaseAuthGateway();
        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(gateway, new FakeEmailSender(), auditPublisher);
        var currentUser = new FakeCurrentUser(Guid.NewGuid(), ownerEmail);

        var result = await handler.HandleAsync(Request(targetId), currentUser, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(targetEmail, result.Value.AdministratorEmail);
        Assert.Contains(supabaseId, gateway.MfaFactorRemovals);
        Assert.Single(auditPublisher.PublishedEvents,
            e => e is PlatformAdministratorMfaResetAuditEvent { Succeeded: true });
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_And_Publishes_Failure_Audit_When_Target_Has_No_Linked_Identity()
    {
        var ownerEmail = await SeedOwnerAsync();
        var (targetId, _, _) = await SeedTargetAsync(supabaseAuthUserId: null);
        var gateway = new FakeSupabaseAuthGateway();
        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(gateway, new FakeEmailSender(), auditPublisher);
        var currentUser = new FakeCurrentUser(Guid.NewGuid(), ownerEmail);

        var result = await handler.HandleAsync(Request(targetId), currentUser, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Empty(gateway.MfaFactorRemovals);
        Assert.Single(auditPublisher.PublishedEvents,
            e => e is PlatformAdministratorMfaResetAuditEvent { Succeeded: false });
    }

    [Fact]
    public async Task HandleAsync_Returns_Unexpected_With_Generic_Message_When_Gateway_Throws()
    {
        var ownerEmail = await SeedOwnerAsync();
        var (targetId, _, _) = await SeedTargetAsync(supabaseAuthUserId: Guid.NewGuid());
        var gateway = new FakeSupabaseAuthGateway { ShouldThrowOnRemoveMfaFactors = true };
        var auditPublisher = new FakeAuditEventPublisher();
        var emailSender = new FakeEmailSender();
        var handler = BuildHandler(gateway, emailSender, auditPublisher);
        var currentUser = new FakeCurrentUser(Guid.NewGuid(), ownerEmail);

        var result = await handler.HandleAsync(Request(targetId), currentUser, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unexpected", result.Error.Code);
        Assert.Equal(ProviderRejectedMessage, result.Error.Message);
        Assert.DoesNotContain("Response body", result.Error.Message);
        Assert.Empty(emailSender.Sent);
        Assert.Single(auditPublisher.PublishedEvents,
            e => e is PlatformAdministratorMfaResetAuditEvent { Succeeded: false });
    }

    [Fact]
    public async Task HandleAsync_Resets_Mfa_Notifies_And_Audits_On_Happy_Path()
    {
        var ownerEmail = await SeedOwnerAsync();
        var actorId = Guid.NewGuid();
        var (targetId, targetEmail, supabaseId) = await SeedTargetAsync(supabaseAuthUserId: Guid.NewGuid());
        var gateway = new FakeSupabaseAuthGateway { MfaFactorsRemovedToReturn = 3 };
        var emailSender = new FakeEmailSender();
        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(gateway, emailSender, auditPublisher);
        var currentUser = new FakeCurrentUser(actorId, ownerEmail);

        var result = await handler.HandleAsync(
            Request(targetId, "lost authenticator"), currentUser, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(targetId, result.Value.AdministratorId);
        Assert.Equal(targetEmail, result.Value.AdministratorEmail);
        Assert.Equal(3, result.Value.FactorsRemoved);
        Assert.True(result.Value.NotificationDelivered);

        Assert.Contains(supabaseId, gateway.MfaFactorRemovals);

        var email = Assert.Single(emailSender.Sent);
        Assert.Equal(targetEmail, email.ToEmail);

        var audit = Assert.Single(
            auditPublisher.PublishedEvents.OfType<PlatformAdministratorMfaResetAuditEvent>());
        Assert.True(audit.Succeeded);
        Assert.Equal(3, audit.FactorsRemoved);
        Assert.Equal("lost authenticator", audit.Reason);
        Assert.Equal(targetId, audit.AdministratorId);
    }
}
