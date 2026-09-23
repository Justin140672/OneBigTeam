using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Ticket 9: the work-email policy only applies to creating NEW accounts. An account that already
/// exists on a public email domain (created before the policy) must still be able to log in and
/// request a password reset — neither path is restricted.
/// </summary>
[Collection("Integration")]
public class ExistingPublicEmailAccountAccessTests
{
    private readonly ApiWebApplicationFactory _factory;

    public ExistingPublicEmailAccountAccessTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.SupabaseAuthGateway.Reset();
    }

    /// <summary>Seeds a pre-existing, login-capable UserProfile whose email is on a public domain.</summary>
    private async Task<Guid> SeedExistingPublicEmailAccountAsync(string email)
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.UserProfiles.Add(UserProfile.Create(
                userId, supabaseAuthUserId: userId, companyId, email, "Legacy", "User",
                new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero)));
            await db.SaveChangesAsync();
        }

        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Employee, companyId);
        return userId;
    }

    [Theory]
    [InlineData("gmail.com")]
    [InlineData("hotmail.co.uk")]
    public async Task Post_Login_Succeeds_For_Existing_Account_On_Public_Domain(string domain)
    {
        var email = $"legacy.user.{Guid.NewGuid():N}@{domain}";
        var userId = await SeedExistingPublicEmailAccountAsync(email);
        _factory.SupabaseAuthGateway.UserIdToReturn = userId;

        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/login", new { email, password = "P@ssw0rd123" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(_factory.SupabaseAuthGateway.SignedInUsers, u => u.Email == email);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("work_email_required", body);
    }

    [Theory]
    [InlineData("gmail.com")]
    [InlineData("outlook.com")]
    public async Task Post_ForgotPassword_Generates_Recovery_Link_For_Existing_Account_On_Public_Domain(string domain)
    {
        var email = $"legacy.user.{Guid.NewGuid():N}@{domain}";
        await SeedExistingPublicEmailAccountAsync(email);

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
}
