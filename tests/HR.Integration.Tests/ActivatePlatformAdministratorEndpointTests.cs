using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// P1: POST /api/identity/platform-administrators/activate deliberately requires only an
/// authenticated caller (no "platform:admin" policy) — see ActivatePlatformAdministratorHandler's
/// remarks. TestAuthHandler's email claim is what the handler matches against the seeded
/// PlatformAdministrator row's email.
/// </summary>
[Collection("Integration")]
public class ActivatePlatformAdministratorEndpointTests
{
    private const string Url = "/api/identity/platform-administrators/activate";

    private readonly ApiWebApplicationFactory _factory;

    public ActivatePlatformAdministratorEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.SupabaseAuthGateway.Reset();
    }

    [Fact]
    public async Task Post_Activate_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(Url, content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_Activate_Links_The_Callers_SupabaseAuthUserId_And_Returns_Ok()
    {
        var email = $"activate-{Guid.NewGuid():N}@test.example";
        var (administratorId, _) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.SupportStaff, email: email);

        // Move the seeded row into a Pending* provisioning state (SeedAdministratorAsync always
        // produces Active) directly via the domain API, mirroring what CreatePlatformAdministrator
        // would have left behind.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var admin = await db.PlatformAdministrators.SingleAsync(a => a.Id == administratorId);
            admin.BeginProvisioning(isNewIdentityProviderAccount: true, Guid.NewGuid(), DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        var supabaseUserId = Guid.NewGuid();
        using var client = PlatformAdministratorTestHelpers.ClientFor(_factory, supabaseUserId, email);

        var response = await client.PostAsync(Url, content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ActivatePayload>();
        Assert.NotNull(payload);
        Assert.Equal(administratorId, payload!.Id);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var reloaded = await verifyDb.PlatformAdministrators.SingleAsync(a => a.Id == administratorId);
        Assert.Equal(PlatformAdministratorProvisioningStatus.Active, reloaded.ProvisioningStatus);
        Assert.Equal(supabaseUserId, reloaded.SupabaseAuthUserId);
    }

    [Fact]
    public async Task Post_Activate_Returns_Conflict_On_Replayed_Activation()
    {
        var email = $"replay-{Guid.NewGuid():N}@test.example";
        var (administratorId, _) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.SupportStaff, email: email);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var admin = await db.PlatformAdministrators.SingleAsync(a => a.Id == administratorId);
            admin.BeginProvisioning(isNewIdentityProviderAccount: true, Guid.NewGuid(), DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        var supabaseUserId = Guid.NewGuid();
        using var client = PlatformAdministratorTestHelpers.ClientFor(_factory, supabaseUserId, email);

        var first = await client.PostAsync(Url, content: null);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PostAsync(Url, content: null);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    private sealed record ActivatePayload(Guid Id, string Email);
}
