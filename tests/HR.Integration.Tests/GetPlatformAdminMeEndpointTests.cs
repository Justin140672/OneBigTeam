using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetPlatformAdminMeEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public GetPlatformAdminMeEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_PlatformAdminMe_Returns_Ok_With_Email_And_Role_For_Enabled_Administrator()
    {
        var (_, email) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.SupportStaff);
        using var client = PlatformAdministratorTestHelpers.ClientFor(_factory, Guid.NewGuid(), email);

        var response = await client.GetAsync("/api/platform-admin/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<PlatformAdminMePayload>();
        Assert.NotNull(payload);
        Assert.Equal(email, payload!.Email);
        Assert.Equal(nameof(PlatformAdministratorRole.SupportStaff), payload.Role);
    }

    [Fact]
    public async Task Get_PlatformAdminMe_Returns_Ok_With_PlatformOwner_Role()
    {
        var (_, email) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.PlatformOwner);
        using var client = PlatformAdministratorTestHelpers.ClientFor(_factory, Guid.NewGuid(), email);

        var response = await client.GetAsync("/api/platform-admin/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<PlatformAdminMePayload>();
        Assert.NotNull(payload);
        Assert.Equal(nameof(PlatformAdministratorRole.PlatformOwner), payload!.Role);
    }

    [Fact]
    public async Task Get_PlatformAdminMe_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/platform-admin/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_PlatformAdminMe_Returns_Forbidden_When_Caller_Is_Not_An_Administrator()
    {
        using var client = PlatformAdministratorTestHelpers.ClientFor(_factory, Guid.NewGuid(), "not-an-admin@test.example");

        var response = await client.GetAsync("/api/platform-admin/me");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_PlatformAdminMe_Returns_Forbidden_When_Caller_Is_Disabled()
    {
        var (_, disabledEmail) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.SupportStaff, isEnabled: false);
        using var client = PlatformAdministratorTestHelpers.ClientFor(_factory, Guid.NewGuid(), disabledEmail);

        var response = await client.GetAsync("/api/platform-admin/me");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_PlatformAdminMe_Matches_By_SupabaseAuthUserId_When_Email_Does_Not_Match()
    {
        var supabaseUserId = Guid.NewGuid();
        var (_, seededEmail) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.SupportStaff, supabaseAuthUserId: supabaseUserId);

        using var client = PlatformAdministratorTestHelpers.ClientFor(
            _factory, supabaseUserId, "someone-else@test.example");

        var response = await client.GetAsync("/api/platform-admin/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<PlatformAdminMePayload>();
        Assert.NotNull(payload);
        Assert.Equal(supabaseUserId, payload!.UserId);
        Assert.Equal(seededEmail, payload.Email);
    }

    private sealed record PlatformAdminMePayload(Guid UserId, string Email, string Role);
}
