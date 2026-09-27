using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.RequestPasswordReset;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Identity.Tests;

/// <summary>
/// CodeQL #34 (clear-text logging of sensitive information): <see cref="RequestPasswordResetHandler"/>
/// handles an email address, a live Supabase recovery action URL (containing a single-use recovery
/// token), the redirect URL and the caller's user agent. Only the boolean dispatch outcome
/// (EmailSent) may reach the log — never the address, its local part, the recovery link/token,
/// the redirect URL or the user agent, on any logging channel (message, structured state, scopes,
/// exception). Also covers the no-matching-profile path.
/// </summary>
[Collection("IdentityDatabase")]
public class RequestPasswordResetLogExposureTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTime Now = new(2026, 6, 6, 12, 0, 0, DateTimeKind.Utc);
    private const string RecoveryToken = "RECOVERY-TOKEN-XYZ";
    private const string ActionUrl = "https://proj.supabase.co/auth/v1/verify?token=RECOVERY-TOKEN-XYZ&type=recovery";
    private const string UserAgent = "Mozilla/5.0 (LogExposureProbe) Gecko/20100101 ProbeBrowser/42.0";

    private RequestPasswordResetHandler BuildHandler(
        FakeSupabaseAuthGateway gateway,
        FakePasswordResetEmailSender emailSender,
        CapturingLogger<RequestPasswordResetHandler> logger) =>
        new(
            fixture.BuildContext(),
            gateway,
            emailSender,
            new ConfigurationBuilder().Build(),
            logger);

    private async Task SeedProfileAsync(string email)
    {
        await using var db = fixture.BuildContext();
        db.UserProfiles.Add(UserProfile.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), email, "Ada", "Lovelace", Now));
        await db.SaveChangesAsync();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Dispatch_Log_Contains_Only_The_EmailSent_Outcome(bool senderResult)
    {
        var localPart = $"reset-probe-{Guid.NewGuid():N}";
        var email = $"{localPart}@example.com";
        await SeedProfileAsync(email);

        var gateway = new FakeSupabaseAuthGateway { RecoveryLinkToReturn = ActionUrl };
        var emailSender = new FakePasswordResetEmailSender(succeeds: senderResult);
        var logger = new CapturingLogger<RequestPasswordResetHandler>();
        var handler = BuildHandler(gateway, emailSender, logger);

        var result = await handler.HandleAsync(new RequestPasswordResetRequest(email, UserAgent), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var sent = Assert.Single(emailSender.Sent);
        Assert.Equal(ActionUrl, sent.ActionUrl); // the link really flowed through the handler
        var generated = Assert.Single(gateway.RecoveryLinksGenerated);

        AssertNoSensitiveText(logger.AllText, email, localPart, generated.RedirectTo);

        var dispatchEntry = Assert.Single(logger.Entries, e => e.StateValue("EmailSent") is not null);
        Assert.Equal(senderResult ? "True" : "False", dispatchEntry.StateValue("EmailSent"));
    }

    [Fact]
    public async Task No_Matching_Profile_Log_Contains_No_Email_Address()
    {
        var localPart = $"missing-probe-{Guid.NewGuid():N}";
        var email = $"{localPart}@example.com";

        var gateway = new FakeSupabaseAuthGateway { RecoveryLinkToReturn = ActionUrl };
        var emailSender = new FakePasswordResetEmailSender();
        var logger = new CapturingLogger<RequestPasswordResetHandler>();
        var handler = BuildHandler(gateway, emailSender, logger);

        var result = await handler.HandleAsync(new RequestPasswordResetRequest(email, UserAgent), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(gateway.RecoveryLinksGenerated);
        Assert.Empty(emailSender.Sent);
        Assert.NotEmpty(logger.Entries);
        Assert.DoesNotContain(logger.Entries, e => e.StateValue("EmailSent") is not null);

        AssertNoSensitiveText(logger.AllText, email, localPart, redirectTo: null);
    }

    private static void AssertNoSensitiveText(string allText, string email, string localPart, string? redirectTo)
    {
        var forbidden = new List<string>
        {
            email,
            localPart,
            "@example.com",
            RecoveryToken,
            "token=",
            "verify?",
            ActionUrl,
            "/reset-password",
            UserAgent,
            "ProbeBrowser",
        };
        if (redirectTo is not null)
            forbidden.Add(redirectTo);

        foreach (var fragment in forbidden)
        {
            Assert.False(
                allText.Contains(fragment, StringComparison.OrdinalIgnoreCase),
                $"Password reset log output exposed '{fragment}'. Captured log text:\n{allText}");
        }
    }
}
