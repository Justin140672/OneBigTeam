using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.ResendVerification;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class ResendVerificationHandlerTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTime Now = new(2026, 6, 6, 12, 0, 0, DateTimeKind.Utc);
    private static readonly IConfiguration EmptyConfiguration = new ConfigurationBuilder().Build();

    private ResendVerificationHandler BuildHandler(FakeSupabaseAuthGateway gateway, IConfiguration? configuration = null) =>
        new(fixture.BuildContext(), gateway, configuration ?? EmptyConfiguration);

    [Fact]
    public async Task HandleAsync_Calls_Gateway_And_Returns_Success_When_Profile_Exists()
    {
        var email = $"ada-{Guid.NewGuid():N}@example.com";

        await using (var db = fixture.BuildContext())
        {
            db.UserProfiles.Add(UserProfile.Create(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), email, "Ada", "Lovelace", Now));
            await db.SaveChangesAsync();
        }

        var gateway = new FakeSupabaseAuthGateway();
        var handler = BuildHandler(gateway);

        var result = await handler.HandleAsync(new ResendVerificationRequest(email), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Success);

        var resent = Assert.Single(gateway.ResentEmails);
        Assert.Equal(email, resent.Email);
        Assert.EndsWith("/verify-email", resent.RedirectTo);
    }

    [Fact]
    public async Task HandleAsync_Matches_Profile_Case_Insensitively()
    {
        var email = $"ada-{Guid.NewGuid():N}@example.com";

        await using (var db = fixture.BuildContext())
        {
            db.UserProfiles.Add(UserProfile.Create(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), email, "Ada", "Lovelace", Now));
            await db.SaveChangesAsync();
        }

        var gateway = new FakeSupabaseAuthGateway();
        var handler = BuildHandler(gateway);

        var result = await handler.HandleAsync(
            new ResendVerificationRequest(email.ToUpperInvariant()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var resent = Assert.Single(gateway.ResentEmails);
        Assert.Equal(email, resent.Email);
    }

    [Fact]
    public async Task HandleAsync_Uses_Configured_WebBaseUrl_When_Present()
    {
        var email = $"ada-{Guid.NewGuid():N}@example.com";

        await using (var db = fixture.BuildContext())
        {
            db.UserProfiles.Add(UserProfile.Create(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), email, "Ada", "Lovelace", Now));
            await db.SaveChangesAsync();
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["services:web:https:0"] = "https://web.example.test",
            })
            .Build();

        var gateway = new FakeSupabaseAuthGateway();
        var handler = BuildHandler(gateway, configuration);

        var result = await handler.HandleAsync(new ResendVerificationRequest(email), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var resent = Assert.Single(gateway.ResentEmails);
        Assert.Equal("https://web.example.test/verify-email", resent.RedirectTo);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Call_Gateway_And_Still_Returns_Success_When_Profile_Not_Found()
    {
        var email = $"missing-{Guid.NewGuid():N}@example.com";
        var gateway = new FakeSupabaseAuthGateway();
        var handler = BuildHandler(gateway);

        var result = await handler.HandleAsync(new ResendVerificationRequest(email), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Success);
        Assert.Empty(gateway.ResentEmails);
    }
}
