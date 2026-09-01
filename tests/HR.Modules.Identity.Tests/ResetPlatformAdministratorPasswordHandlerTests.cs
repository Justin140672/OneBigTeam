using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.ResetPlatformAdministratorPassword;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class ResetPlatformAdministratorPasswordHandlerTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly FakeClock Clock = new(Now.UtcDateTime);
    private static readonly IConfiguration EmptyConfiguration = new ConfigurationBuilder().Build();

    private ResetPlatformAdministratorPasswordHandler BuildHandler(
        FakeSupabaseAuthGateway gateway,
        FakeAuditEventPublisher auditPublisher,
        FakePasswordResetEmailSender? emailSender = null) =>
        new(fixture.BuildContext(), gateway, emailSender ?? new FakePasswordResetEmailSender(),
            EmptyConfiguration, Clock, auditPublisher);

    private async Task<string> SeedOwnerAsync()
    {
        var email = $"owner-{Guid.NewGuid():N}@test.com";
        await using var db = fixture.BuildContext();
        db.PlatformAdministrators.Add(PlatformAdministrator.Create(email, PlatformAdministratorRole.PlatformOwner, Now));
        await db.SaveChangesAsync();
        return email;
    }

    private async Task<(Guid Id, string Email)> SeedTargetAsync()
    {
        var email = $"target-{Guid.NewGuid():N}@test.com";
        await using var db = fixture.BuildContext();
        var target = PlatformAdministrator.Create(email, PlatformAdministratorRole.SupportStaff, Now);
        db.PlatformAdministrators.Add(target);
        await db.SaveChangesAsync();
        return (target.Id, target.Email);
    }

    [Fact]
    public async Task HandleAsync_Returns_Unauthorized_When_Caller_Is_Not_A_PlatformOwner()
    {
        var (targetId, _) = await SeedTargetAsync();
        var handler = BuildHandler(new FakeSupabaseAuthGateway(), new FakeAuditEventPublisher());
        var currentUser = new FakeCurrentUser(Guid.NewGuid(), "not-an-owner@test.com");

        var result = await handler.HandleAsync(
            new ResetPlatformAdministratorPasswordRequest(targetId), currentUser, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unauthorized", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Administrator_Missing()
    {
        var ownerEmail = await SeedOwnerAsync();
        var handler = BuildHandler(new FakeSupabaseAuthGateway(), new FakeAuditEventPublisher());
        var currentUser = new FakeCurrentUser(Guid.NewGuid(), ownerEmail);

        var result = await handler.HandleAsync(
            new ResetPlatformAdministratorPasswordRequest(Guid.NewGuid()), currentUser, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Generates_Recovery_Link_Sends_Branded_Email_And_Audits_On_Happy_Path()
    {
        var ownerEmail = await SeedOwnerAsync();
        var (targetId, targetEmail) = await SeedTargetAsync();
        var gateway = new FakeSupabaseAuthGateway
        {
            RecoveryLinkToReturn = "https://proj.supabase.co/auth/v1/verify?token=single-use&type=recovery",
        };
        var emailSender = new FakePasswordResetEmailSender();
        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(gateway, auditPublisher, emailSender);
        var currentUser = new FakeCurrentUser(Guid.NewGuid(), ownerEmail);

        var result = await handler.HandleAsync(
            new ResetPlatformAdministratorPasswordRequest(targetId), currentUser, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Requested);

        // No longer the client-facing /auth/v1/recover path.
        Assert.Empty(gateway.PasswordResetRequests);

        var generated = Assert.Single(gateway.RecoveryLinksGenerated);
        Assert.Equal(targetEmail, generated.Email);
        Assert.EndsWith("/reset-password", generated.RedirectTo);

        var sent = Assert.Single(emailSender.Sent);
        Assert.Equal(targetEmail, sent.ToEmail);
        Assert.Equal("https://proj.supabase.co/auth/v1/verify?token=single-use&type=recovery", sent.ActionUrl);

        Assert.Single(auditPublisher.PublishedEvents, e => e is PlatformAdministratorPasswordResetRequestedAuditEvent);
    }
}
