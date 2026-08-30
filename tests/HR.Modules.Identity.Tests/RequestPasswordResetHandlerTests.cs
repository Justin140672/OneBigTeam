using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.RequestPasswordReset;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class RequestPasswordResetHandlerTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTime Now = new(2026, 6, 6, 12, 0, 0, DateTimeKind.Utc);
    private static readonly IConfiguration EmptyConfiguration = new ConfigurationBuilder().Build();

    private RequestPasswordResetHandler BuildHandler(
        FakeSupabaseAuthGateway gateway,
        FakePasswordResetEmailSender emailSender,
        IConfiguration? configuration = null) =>
        new(
            fixture.BuildContext(),
            gateway,
            emailSender,
            configuration ?? EmptyConfiguration,
            NullLogger<RequestPasswordResetHandler>.Instance);

    private async Task SeedProfileAsync(string email, string first = "Ada", string last = "Lovelace")
    {
        await using var db = fixture.BuildContext();
        db.UserProfiles.Add(UserProfile.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), email, first, last, Now));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Generates_Recovery_Link_And_Sends_Branded_Email_When_Profile_Exists()
    {
        var email = $"ada-{Guid.NewGuid():N}@example.com";
        await SeedProfileAsync(email, "Ada", "Lovelace");

        var gateway = new FakeSupabaseAuthGateway
        {
            RecoveryLinkToReturn = "https://proj.supabase.co/auth/v1/verify?token=real-token&type=recovery",
        };
        var emailSender = new FakePasswordResetEmailSender();
        var handler = BuildHandler(gateway, emailSender);

        var result = await handler.HandleAsync(
            new RequestPasswordResetRequest(email, "Mozilla/5.0 (Windows NT 10.0) Chrome/120.0 Safari/537.36"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Success);

        var generated = Assert.Single(gateway.RecoveryLinksGenerated);
        Assert.Equal(email, generated.Email);
        Assert.EndsWith("/reset-password", generated.RedirectTo);

        var sent = Assert.Single(emailSender.Sent);
        Assert.Equal(email, sent.ToEmail);
        Assert.Equal("Ada Lovelace", sent.RecipientName);
        Assert.Equal("https://proj.supabase.co/auth/v1/verify?token=real-token&type=recovery", sent.ActionUrl);
        Assert.Equal("Mozilla/5.0 (Windows NT 10.0) Chrome/120.0 Safari/537.36", sent.UserAgent);
    }

    [Fact]
    public async Task Uses_Configured_WebApp_BaseUrl_For_Redirect()
    {
        var email = $"ada-{Guid.NewGuid():N}@example.com";
        await SeedProfileAsync(email);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WebApp:BaseUrl"] = "https://app.example.test/",
            })
            .Build();

        var gateway = new FakeSupabaseAuthGateway();
        var handler = BuildHandler(gateway, new FakePasswordResetEmailSender(), configuration);

        await handler.HandleAsync(new RequestPasswordResetRequest(email), CancellationToken.None);

        var generated = Assert.Single(gateway.RecoveryLinksGenerated);
        Assert.Equal("https://app.example.test/reset-password", generated.RedirectTo);
    }

    [Fact]
    public async Task Matches_Profile_Case_Insensitively()
    {
        var email = $"ada-{Guid.NewGuid():N}@example.com";
        await SeedProfileAsync(email);

        var gateway = new FakeSupabaseAuthGateway();
        var emailSender = new FakePasswordResetEmailSender();
        var handler = BuildHandler(gateway, emailSender);

        var result = await handler.HandleAsync(
            new RequestPasswordResetRequest(email.ToUpperInvariant()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(emailSender.Sent);
    }

    [Fact]
    public async Task Does_Not_Generate_Link_Or_Send_Email_But_Still_Returns_Success_When_Profile_Missing()
    {
        var email = $"missing-{Guid.NewGuid():N}@example.com";
        var gateway = new FakeSupabaseAuthGateway();
        var emailSender = new FakePasswordResetEmailSender();
        var handler = BuildHandler(gateway, emailSender);

        var result = await handler.HandleAsync(new RequestPasswordResetRequest(email), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Success);
        Assert.Empty(gateway.RecoveryLinksGenerated);
        Assert.Empty(emailSender.Sent);
    }

    [Fact]
    public async Task Sends_Null_Name_When_Profile_Has_No_Name()
    {
        var email = $"ada-{Guid.NewGuid():N}@example.com";
        await SeedProfileAsync(email, first: "", last: "");

        var gateway = new FakeSupabaseAuthGateway();
        var emailSender = new FakePasswordResetEmailSender();
        var handler = BuildHandler(gateway, emailSender);

        await handler.HandleAsync(new RequestPasswordResetRequest(email), CancellationToken.None);

        var sent = Assert.Single(emailSender.Sent);
        Assert.Null(sent.RecipientName);
    }
}
