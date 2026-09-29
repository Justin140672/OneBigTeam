using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Ticket 9: POST /api/platform-administrators must refuse a public/disposable address for a NEW
/// platform administrator (400 work_email_required) before any platform_administrators row,
/// provider account or onboarding email is created. Existing administrators on public domains are
/// unaffected. See CreatePlatformAdministratorEndpointTests for the general contract.
/// </summary>
[Collection("Integration")]
public class CreatePlatformAdministratorWorkEmailPolicyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public CreatePlatformAdministratorWorkEmailPolicyEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.SupabaseAuthGateway.Reset();
    }

    [Fact]
    public async Task Post_PlatformAdministrators_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/platform-administrators", new { email = "new-admin@gmail.com", role = "SupportStaff" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("gmail.com")]
    [InlineData("HOTMAIL.COM")]
    [InlineData("icloud.com")]
    public async Task Post_PlatformAdministrators_Returns_400_WorkEmailRequired_For_Public_Domain_And_Creates_Nothing(string domain)
    {
        var (_, ownerEmail) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.PlatformOwner);
        using var client = PlatformAdministratorTestHelpers.ClientFor(_factory, Guid.NewGuid(), ownerEmail);
        var newEmail = $"new-admin-{Guid.NewGuid():N}@{domain}";

        var response = await client.PostAsJsonAsync(
            "/api/platform-administrators", new { email = newEmail, role = "SupportStaff" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("work_email_required", doc.RootElement.GetProperty("code").GetString());
        Assert.StartsWith("Please use your organisation's work email address.", doc.RootElement.GetProperty("error").GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var normalised = newEmail.ToLowerInvariant();
        Assert.False(await db.PlatformAdministrators.AnyAsync(a => a.Email == normalised));

        Assert.DoesNotContain(_factory.SupabaseAuthGateway.PendingUsersCreatedWithMetadata, u => string.Equals(u.Email, newEmail, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(_factory.SupabaseAuthGateway.ConfirmedUsersCreated, u => string.Equals(u.Email, newEmail, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(_factory.SupabaseAuthGateway.CreatedUsers, u => string.Equals(u.Email, newEmail, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(_factory.SupabaseAuthGateway.RecoveryLinksGenerated, u => string.Equals(u.Email, newEmail, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(_factory.EmailSender.Sent, e => string.Equals(e.ToEmail, newEmail, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Post_PlatformAdministrators_Succeeds_For_Organisation_Domain()
    {
        var (_, ownerEmail) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.PlatformOwner);
        using var client = PlatformAdministratorTestHelpers.ClientFor(_factory, Guid.NewGuid(), ownerEmail);
        var newEmail = $"new-admin-{Guid.NewGuid():N}@acme.example";

        var response = await client.PostAsJsonAsync(
            "/api/platform-administrators", new { email = newEmail, role = "SupportStaff" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.True(await db.PlatformAdministrators.AnyAsync(a => a.Email == newEmail));
    }

    [Fact]
    public async Task Post_PlatformAdministrators_Existing_Owner_On_Public_Domain_Can_Still_Create_Org_Domain_Administrator()
    {
        var (_, ownerEmail) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.PlatformOwner, email: $"legacy-owner-{Guid.NewGuid():N}@gmail.com");
        using var client = PlatformAdministratorTestHelpers.ClientFor(_factory, Guid.NewGuid(), ownerEmail);
        var newEmail = $"new-admin-{Guid.NewGuid():N}@brightsparks-consulting.co.uk";

        var response = await client.PostAsJsonAsync(
            "/api/platform-administrators", new { email = newEmail, role = "SupportStaff" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.True(await db.PlatformAdministrators.AnyAsync(a => a.Email == newEmail));
    }
}
