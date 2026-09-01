using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.Login;
using HR.Modules.Identity.Features.RequestPasswordReset;
using HR.Modules.Identity.Features.ResetPassword;
using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Identity.Tests;

/// <summary>
/// Ticket 2 — passwords, access/refresh tokens, JWTs and single-use recovery links must never
/// reach application logs or user-facing error messages from the auth/password-reset flows.
/// </summary>
[Collection("IdentityDatabase")]
public class SensitiveAuthLoggingTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTime Now = new(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc);
    private static readonly FakeClock Clock = new(Now);

    // Representative token-shaped values — a JWT, an opaque refresh token and a recovery URL.
    private const string TokenShapedPassword = "eyJhbGciOiJIUzI1NiJ9.eyJwd2QiOiJzZWNyZXQifQ.sig-value-123";
    private const string RecoveryUrlWithToken =
        "https://proj.supabase.co/auth/v1/verify?token=pkce_9f8e7d6c5b4a3210deadbeef&type=recovery";

    [Fact]
    public async Task LoginHandler_Does_Not_Log_Password_Or_Full_Email_When_SignIn_Fails()
    {
        var logger = new ListLogger<LoginHandler>();
        var gateway = new FakeSupabaseAuthGateway { ShouldThrowOnSignIn = true };

        var services = new ServiceCollection();
        services.AddDbContext<IdentityDbContext>(o =>
            o.UseNpgsql(fixture.ConnectionString, n => n.MigrationsHistoryTable("__ef_migrations_history", "identity")));
        services.AddSingleton<IClock>(Clock);
        await using var serviceProvider = services.BuildServiceProvider();

        var handler = new LoginHandler(
            gateway,
            fixture.BuildContext(),
            serviceProvider,
            new IdentityAuthorizationService(fixture.BuildContext(), Clock),
            logger);

        var result = await handler.HandleAsync(
            new LoginRequest("secret.person@example.com", TokenShapedPassword), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.DoesNotContain(TokenShapedPassword, logger.Text);
        Assert.DoesNotContain("secret.person@example.com", logger.Text);
        Assert.Contains("se***@example.com", logger.Text);
    }

    [Fact]
    public async Task RequestPasswordResetHandler_Does_Not_Log_Recovery_Link_Or_Token()
    {
        var logger = new ListLogger<RequestPasswordResetHandler>();
        var email = $"ada-{Guid.NewGuid():N}@example.com";

        await using (var db = fixture.BuildContext())
        {
            db.UserProfiles.Add(UserProfile.Create(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), email, "Ada", "Lovelace", Now));
            await db.SaveChangesAsync();
        }

        var gateway = new FakeSupabaseAuthGateway { RecoveryLinkToReturn = RecoveryUrlWithToken };
        var handler = new RequestPasswordResetHandler(
            fixture.BuildContext(),
            gateway,
            new FakePasswordResetEmailSender(),
            new ConfigurationBuilder().Build(),
            logger);

        var result = await handler.HandleAsync(new RequestPasswordResetRequest(email), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(RecoveryUrlWithToken, logger.Text);
        Assert.DoesNotContain("pkce_9f8e7d6c5b4a3210deadbeef", logger.Text);
    }

    [Fact]
    public async Task ResetPasswordHandler_Returns_Generic_Message_Without_Gateway_Token_Details()
    {
        var gateway = new FakeSupabaseAuthGateway { ShouldThrowOnUpdatePassword = true };
        var handler = new ResetPasswordHandler(gateway);

        var result = await handler.HandleAsync(
            new ResetPasswordRequest("eyJhbGciOiJIUzI1NiJ9.user-access-token.sig", "NewPassw0rd!"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(
            "This link is invalid or has expired. Please request a new password reset email.",
            result.Error.Message);
        Assert.DoesNotContain("access_token", result.Error.Message);
        Assert.DoesNotContain("eyJhbGci", result.Error.Message);
    }

    [Fact]
    public void MaskEmail_Keeps_Domain_But_Hides_Local_Part()
    {
        Assert.Equal("jo***@example.com", SensitiveDataScrubber.MaskEmail("john.doe@example.com"));
        Assert.Equal("a***@example.com", SensitiveDataScrubber.MaskEmail("a@example.com"));
        Assert.Equal(SensitiveDataScrubber.Redacted, SensitiveDataScrubber.MaskEmail("not-an-email"));
        Assert.Equal(SensitiveDataScrubber.Redacted, SensitiveDataScrubber.MaskEmail(null));
    }
}
