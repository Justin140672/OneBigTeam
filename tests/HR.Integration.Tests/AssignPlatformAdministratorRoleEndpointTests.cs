using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// See CreatePlatformAdministratorEndpointTests for notes on the "platform:admin" policy /
/// handler-level PlatformOwner gate and the 401-for-both-anonymous-and-non-owner behavior.
/// </summary>
[Collection("Integration")]
public class AssignPlatformAdministratorRoleEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public AssignPlatformAdministratorRoleEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Post_AssignPlatformAdministratorRole_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/platform-administrators/{Guid.NewGuid()}/role", new { role = "PlatformOwner" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_AssignPlatformAdministratorRole_Returns_Unauthorized_When_Caller_Is_Not_A_PlatformOwner()
    {
        using var client = PlatformAdministratorTestHelpers.ClientFor(_factory, Guid.NewGuid(), "not-an-owner@test.example");

        var response = await client.PostAsJsonAsync(
            $"/api/platform-administrators/{Guid.NewGuid()}/role", new { role = "PlatformOwner" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_AssignPlatformAdministratorRole_Returns_NotFound_When_Administrator_Missing()
    {
        var (_, ownerEmail) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.PlatformOwner);
        using var client = PlatformAdministratorTestHelpers.ClientFor(_factory, Guid.NewGuid(), ownerEmail);

        var response = await client.PostAsJsonAsync(
            $"/api/platform-administrators/{Guid.NewGuid()}/role", new { role = "PlatformOwner" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_AssignPlatformAdministratorRole_Returns_UnprocessableEntity_When_Role_Is_Invalid()
    {
        var (_, ownerEmail) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.PlatformOwner);
        var (targetId, _) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.SupportStaff);
        using var client = PlatformAdministratorTestHelpers.ClientFor(_factory, Guid.NewGuid(), ownerEmail);

        var response = await client.PostAsJsonAsync(
            $"/api/platform-administrators/{targetId}/role", new { role = 999 });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_AssignPlatformAdministratorRole_Changes_Role_On_Happy_Path()
    {
        var (_, ownerEmail) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.PlatformOwner);
        var (targetId, _) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.SupportStaff);
        using var client = PlatformAdministratorTestHelpers.ClientFor(_factory, Guid.NewGuid(), ownerEmail);

        var response = await client.PostAsJsonAsync(
            $"/api/platform-administrators/{targetId}/role", new { role = "PlatformOwner" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<RolePayload>();
        Assert.NotNull(payload);
        Assert.Equal("PlatformOwner", payload!.Role);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var reloaded = await db.PlatformAdministrators.FirstAsync(a => a.Id == targetId);
        Assert.Equal(PlatformAdministratorRole.PlatformOwner, reloaded.Role);
    }

    private sealed record RolePayload(Guid Id, string Role);
}
