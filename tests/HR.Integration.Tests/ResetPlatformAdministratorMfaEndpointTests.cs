using System.Net;
using System.Net.Http.Json;
using System.Text;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// See CreatePlatformAdministratorEndpointTests for notes on the "platform:admin" policy /
/// handler-level PlatformOwner gate and the 401-for-both-anonymous-and-non-owner behavior. This
/// endpoint is a deliberate stub — it only records an audit event and always returns
/// Implemented: false; see ResetPlatformAdministratorMfaHandler's remarks.
/// </summary>
[Collection("Integration")]
public class ResetPlatformAdministratorMfaEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public ResetPlatformAdministratorMfaEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static StringContent EmptyJson() => new("{}", Encoding.UTF8, "application/json");

    [Fact]
    public async Task Post_ResetPlatformAdministratorMfa_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(
            $"/api/platform-administrators/{Guid.NewGuid()}/reset-mfa", EmptyJson());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_ResetPlatformAdministratorMfa_Returns_Unauthorized_When_Caller_Is_Not_A_PlatformOwner()
    {
        using var client = PlatformAdministratorTestHelpers.ClientFor(_factory, Guid.NewGuid(), "not-an-owner@test.example");

        var response = await client.PostAsync(
            $"/api/platform-administrators/{Guid.NewGuid()}/reset-mfa", EmptyJson());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_ResetPlatformAdministratorMfa_Returns_NotFound_When_Administrator_Missing()
    {
        var (_, ownerEmail) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.PlatformOwner);
        using var client = PlatformAdministratorTestHelpers.ClientFor(_factory, Guid.NewGuid(), ownerEmail);

        var response = await client.PostAsync(
            $"/api/platform-administrators/{Guid.NewGuid()}/reset-mfa", EmptyJson());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_ResetPlatformAdministratorMfa_Returns_Not_Implemented_Stub_Response_On_Happy_Path()
    {
        var (_, ownerEmail) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.PlatformOwner);
        var (targetId, _) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.SupportStaff);
        using var client = PlatformAdministratorTestHelpers.ClientFor(_factory, Guid.NewGuid(), ownerEmail);

        var response = await client.PostAsync(
            $"/api/platform-administrators/{targetId}/reset-mfa", EmptyJson());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ResetMfaPayload>();
        Assert.NotNull(payload);
        Assert.Equal(targetId, payload!.AdministratorId);
        Assert.False(payload.Implemented);
    }

    private sealed record ResetMfaPayload(Guid AdministratorId, bool Implemented);
}
