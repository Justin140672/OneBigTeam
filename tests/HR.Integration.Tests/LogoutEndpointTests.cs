using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;

namespace HR.Integration.Tests;

/// <summary>
/// POST /api/logout — best-effort server-side Supabase session revocation on sign-out (security
/// ticket "Remove authentication tokens from browser-visible URLs"). Anonymous: the caller
/// (HR.Web's /logout) presents the access token from its session cookie as a bearer, which
/// authenticates the request to Supabase's GoTrue logout endpoint, not to this API. The endpoint
/// must always return 200 so a revocation failure never blocks the user's sign-out.
/// </summary>
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
