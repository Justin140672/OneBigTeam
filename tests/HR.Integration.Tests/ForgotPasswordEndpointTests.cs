using System.Net;
using System.Net.Http.Json;
using System.Text;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// POST /api/forgot-password (HR.Modules.Identity RequestPasswordReset). Anonymous endpoint that
/// generates a real Supabase recovery link (faked here) and hands it to the branded email sender.
/// Deliberately non-enumerating: identical 200 response whether or not the email matches an account.
/// </summary>
[Collection("Integration")]
public class ForgotPasswordEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public ForgotPasswordEndpointTests(ApiWebApplicationFactory factory) => _factory = factory;

    private async Task SeedProfileAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        db.UserProfiles.Add(UserProfile.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), email, "Ada", "Lovelace",
            new DateTimeOffset(2026, 6, 6, 12, 0, 0, TimeSpan.Zero)));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Post_ForgotPassword_Generates_Recovery_Link_When_Account_Exists()
    {
        var email = $"ada-{Guid.NewGuid():N}@example.com";
        await SeedProfileAsync(email);
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/forgot-password", new
        {
            Email = email,
            UserAgent = "Mozilla/5.0 (Windows NT 10.0) Chrome/120.0 Safari/537.36",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var generated = Assert.Single(_factory.SupabaseAuthGateway.RecoveryLinksGenerated, r => r.Email == email);
        Assert.EndsWith("/reset-password", generated.RedirectTo);
    }

    [Fact]
    public async Task Post_ForgotPassword_Returns_Ok_And_Generates_Nothing_When_Account_Does_Not_Exist()
    {
        var email = $"nobody-{Guid.NewGuid():N}@example.com";
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/forgot-password", new { Email = email });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(_factory.SupabaseAuthGateway.RecoveryLinksGenerated, r => r.Email == email);
    }

    [Fact]
    public async Task Post_ForgotPassword_Returns_Validation_Error_For_Invalid_Email()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(
            "/api/forgot-password",
            new StringContent("""{"Email":"not-an-email"}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_ResetPassword_Updates_Password_On_Valid_Recovery_Token()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/reset-password", new
        {
            AccessToken = "recovery-token-value",
            NewPassword = "N3w-Passw0rd!",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(_factory.SupabaseAuthGateway.PasswordUpdates, u => u.AccessToken == "recovery-token-value");
    }

    [Fact]
    public async Task Post_ResetPassword_Returns_Validation_Error_When_Token_Missing()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(
            "/api/reset-password",
            new StringContent("""{"NewPassword":"N3w-Passw0rd!"}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}
