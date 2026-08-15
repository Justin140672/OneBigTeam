using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// The "platform:admin" endpoint policy only requires an authenticated caller (no tenant/company
/// header). The handler-level gate additionally requires the caller's email to match an enabled
/// PlatformOwner row in identity.platform_administrators — see
/// PlatformAdministratorTestHelpers/CreatePlatformAdministratorHandler.IsEnabledPlatformOwnerAsync.
/// Note: the handler returns Error.Unauthorized both for anonymous and for authenticated-but-not-
/// PlatformOwner callers, and the endpoint maps that error code to HTTP 401 (not 403) in both
/// cases — see Endpoint.cs's error-code switch.
/// </summary>
[Collection("Integration")]
public class CreatePlatformAdministratorEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public CreatePlatformAdministratorEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Post_PlatformAdministrators_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/platform-administrators", new { email = "new-admin@test.example", role = "SupportStaff" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_PlatformAdministrators_Returns_Unauthorized_When_Caller_Is_Not_A_PlatformOwner()
    {
        var userId = Guid.NewGuid();
        using var client = PlatformAdministratorTestHelpers.ClientFor(_factory, userId, "not-an-owner@test.example");

        var response = await client.PostAsJsonAsync(
            "/api/platform-administrators", new { email = "new-admin2@test.example", role = "SupportStaff" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_PlatformAdministrators_Returns_Unauthorized_When_Caller_Is_SupportStaff_Not_PlatformOwner()
    {
        var (_, supportEmail) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.SupportStaff);
        using var client = PlatformAdministratorTestHelpers.ClientFor(_factory, Guid.NewGuid(), supportEmail);

        var response = await client.PostAsJsonAsync(
            "/api/platform-administrators", new { email = "new-admin3@test.example", role = "SupportStaff" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_PlatformAdministrators_Returns_UnprocessableEntity_When_Email_Is_Invalid()
    {
        var (_, ownerEmail) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.PlatformOwner);
        using var client = PlatformAdministratorTestHelpers.ClientFor(_factory, Guid.NewGuid(), ownerEmail);

        var response = await client.PostAsJsonAsync(
            "/api/platform-administrators", new { email = "not-an-email", role = "SupportStaff" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_PlatformAdministrators_Returns_Conflict_When_Email_Already_Exists()
    {
        var (_, ownerEmail) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.PlatformOwner);
        var (_, existingEmail) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.SupportStaff);
        using var client = PlatformAdministratorTestHelpers.ClientFor(_factory, Guid.NewGuid(), ownerEmail);

        var response = await client.PostAsJsonAsync(
            "/api/platform-administrators", new { email = existingEmail, role = "SupportStaff" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Post_PlatformAdministrators_Creates_Administrator_On_Happy_Path()
    {
        var (_, ownerEmail) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.PlatformOwner);
        using var client = PlatformAdministratorTestHelpers.ClientFor(_factory, Guid.NewGuid(), ownerEmail);

        var newEmail = $"new-admin-{Guid.NewGuid():N}@test.example";

        var response = await client.PostAsJsonAsync(
            "/api/platform-administrators", new { email = newEmail, role = "SupportStaff" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<PlatformAdministratorPayload>();
        Assert.NotNull(payload);
        Assert.Equal(newEmail.ToLowerInvariant(), payload!.Email);
        Assert.Equal("SupportStaff", payload.Role);
        Assert.True(payload.IsEnabled);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var reloaded = await db.PlatformAdministrators.FirstAsync(a => a.Id == payload.Id);
        Assert.Equal(newEmail.ToLowerInvariant(), reloaded.Email);
    }

    private sealed record PlatformAdministratorPayload(Guid Id, string Email, string Role, bool IsEnabled, DateTimeOffset CreatedAt);
}
