using System.Net;
using System.Net.Http.Json;
using System.Text;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// See CreatePlatformAdministratorEndpointTests for notes on the "platform:admin" policy /
/// handler-level PlatformOwner gate and the 401-for-both-anonymous-and-non-owner behavior. This
/// handler now generates a single-use recovery link via ISupabaseAuthGateway.GenerateRecoveryLinkAsync
/// (admin generate_link, not the client-facing /auth/v1/recover) and sends it via the branded
/// password-reset email template; ApiWebApplicationFactory replaces the gateway with
/// FakeSupabaseAuthGateway so no live Supabase call is made.
/// </summary>
[Collection("Integration")]
public class ResetPlatformAdministratorPasswordEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public ResetPlatformAdministratorPasswordEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static StringContent EmptyJson() => new("{}", Encoding.UTF8, "application/json");

    [Fact]
    public async Task Post_ResetPlatformAdministratorPassword_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(
            $"/api/platform-administrators/{Guid.NewGuid()}/reset-password", EmptyJson());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_ResetPlatformAdministratorPassword_Returns_Unauthorized_When_Caller_Is_Not_A_PlatformOwner()
    {
        using var client = PlatformAdministratorTestHelpers.ClientFor(_factory, Guid.NewGuid(), "not-an-owner@test.example");

        var response = await client.PostAsync(
            $"/api/platform-administrators/{Guid.NewGuid()}/reset-password", EmptyJson());

        // See PlatformAdminAuthorizationHandler.cs / f2658d7d — authenticated-but-not-authorized
        // is Forbidden (403), not Unauthorized (401).
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_ResetPlatformAdministratorPassword_Returns_NotFound_When_Administrator_Missing()
    {
        var (_, ownerEmail) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.PlatformOwner);
        using var client = PlatformAdministratorTestHelpers.ClientFor(_factory, Guid.NewGuid(), ownerEmail);

        var response = await client.PostAsync(
            $"/api/platform-administrators/{Guid.NewGuid()}/reset-password", EmptyJson());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_ResetPlatformAdministratorPassword_Requests_Reset_On_Happy_Path()
    {
        var (_, ownerEmail) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.PlatformOwner);
        var (targetId, targetEmail) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.SupportStaff);
        using var client = PlatformAdministratorTestHelpers.ClientFor(_factory, Guid.NewGuid(), ownerEmail);

        var response = await client.PostAsync(
            $"/api/platform-administrators/{targetId}/reset-password", EmptyJson());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ResetPasswordPayload>();
        Assert.NotNull(payload);
        Assert.Equal(targetId, payload!.Id);
        Assert.True(payload.Requested);

        Assert.Contains(_factory.SupabaseAuthGateway.RecoveryLinksGenerated, r => r.Email == targetEmail);
        Assert.DoesNotContain(_factory.SupabaseAuthGateway.PasswordResetRequests, r => r.Email == targetEmail);
    }

    private sealed record ResetPasswordPayload(Guid Id, bool Requested);
}
