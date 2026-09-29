using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class LogoutEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public LogoutEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.SupabaseAuthGateway.Reset();
    }

    [Fact]
    public async Task Post_Logout_Revokes_The_Supabase_Session_For_The_Bearer_Token()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "cookie-access-token");

        var response = await client.PostAsync("/api/logout", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<LogoutPayload>();
        Assert.True(payload!.SignedOut);
        Assert.Equal("cookie-access-token", Assert.Single(_factory.SupabaseAuthGateway.SignOutCalls));
    }

    [Fact]
    public async Task Post_Logout_Returns_Ok_And_Does_Nothing_When_No_Bearer_Is_Present()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/logout", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<LogoutPayload>();
        Assert.False(payload!.SignedOut);
        Assert.Empty(_factory.SupabaseAuthGateway.SignOutCalls);
    }

    [Fact]
    public async Task Post_Logout_Still_Returns_Ok_When_Supabase_Revocation_Fails()
    {
        _factory.SupabaseAuthGateway.ShouldThrowOnSignOut = true;

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "expired-token");

        var response = await client.PostAsync("/api/logout", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<LogoutPayload>();
        Assert.False(payload!.SignedOut);

        _factory.SupabaseAuthGateway.Reset();
    }

    private sealed record LogoutPayload(bool SignedOut);
}
