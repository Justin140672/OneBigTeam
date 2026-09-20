using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// See CreatePlatformAdministratorEndpointTests for notes on the "platform:admin" policy /
/// handler-level PlatformOwner gate and the 401-anonymous / 403-non-owner behavior.
/// POST /api/platform-administrators/{id}/retry-provisioning.
/// </summary>
[Collection("Integration")]
public class RetryPlatformAdministratorProvisioningEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public RetryPlatformAdministratorProvisioningEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.SupabaseAuthGateway.Reset();
    }

    private static string Url(Guid id) => $"/api/platform-administrators/{id}/retry-provisioning";

    [Fact]
    public async Task Post_RetryProvisioning_Returns_Forbidden_When_Caller_Is_Not_A_PlatformOwner()
    {
        using var client = PlatformAdministratorTestHelpers.ClientFor(_factory, Guid.NewGuid(), "not-an-owner@test.example");

        var response = await client.PostAsync(Url(Guid.NewGuid()), content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_RetryProvisioning_Returns_NotFound_For_Unknown_Id()
    {
        var (_, ownerEmail) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.PlatformOwner);
        using var client = PlatformAdministratorTestHelpers.ClientFor(_factory, Guid.NewGuid(), ownerEmail);

        var response = await client.PostAsync(Url(Guid.NewGuid()), content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Drives a genuinely Failed row through the real Create endpoint (ShouldThrowOnCreate = true)
    /// exactly like production would produce one, then resets the gateway and retries.
    /// </summary>
    [Fact]
    public async Task Post_RetryProvisioning_Succeeds_And_Moves_Status_Back_To_Pending_On_Happy_Path()
    {
        var (_, ownerEmail) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.PlatformOwner);
        using var ownerClient = PlatformAdministratorTestHelpers.ClientFor(_factory, Guid.NewGuid(), ownerEmail);

        var targetEmail = $"retry-e2e-{Guid.NewGuid():N}@test.example";
        _factory.SupabaseAuthGateway.ShouldThrowOnCreate = true;

        var createResponse = await ownerClient.PostAsJsonAsync(
            "/api/platform-administrators", new { email = targetEmail, role = "SupportStaff" });
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<CreatedPayload>();
        Assert.NotNull(created);
        Assert.Equal("failed", created!.ProvisioningStatus);

        _factory.SupabaseAuthGateway.ShouldThrowOnCreate = false;

        var retryResponse = await ownerClient.PostAsync(Url(created.Id), content: null);

        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        var retried = await retryResponse.Content.ReadFromJsonAsync<RetryPayload>();
        Assert.NotNull(retried);
        Assert.Equal("pending_provisioning", retried!.ProvisioningStatus);

        Assert.Contains(_factory.SupabaseAuthGateway.PendingUsersCreatedWithMetadata, u => u.Email == targetEmail.ToLowerInvariant());
    }

    private sealed record CreatedPayload(Guid Id, string Email, string Role, bool IsEnabled, DateTimeOffset CreatedAt, string ProvisioningStatus);

    private sealed record RetryPayload(Guid Id, string ProvisioningStatus);
}
