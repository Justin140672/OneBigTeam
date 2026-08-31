using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// Covers POST /api/login for the security ticket "Remove authentication tokens from
/// browser-visible URLs": the API hands the freshly minted Supabase tokens back in the JSON
/// response body (HR.Web then stashes them server-side via AuthHandoffStore and only ever puts an
/// opaque handoff code in a URL). This asserts the endpoint's shape — a JSON 200 with tokens, no
/// redirect, no token in any URL — plus the generic-failure and validation behaviour.
/// </summary>
[Collection("Integration")]
public class LoginEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public LoginEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.SupabaseAuthGateway.Reset();
    }

    private async Task<Guid> SeedLoginCapableUserAsync()
    {
        var userId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Employee, companyId: Guid.NewGuid());
        return userId;
    }

    [Fact]
    public async Task Post_Login_Returns_Tokens_In_The_Json_Body_And_Not_A_Redirect()
    {
        var userId = await SeedLoginCapableUserAsync();
        _factory.SupabaseAuthGateway.UserIdToReturn = userId;

        using var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.PostAsJsonAsync("/api/login", new { email = "ada@example.com", password = "P@ssw0rd123" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var payload = await response.Content.ReadFromJsonAsync<LoginPayload>();
        Assert.NotNull(payload);
        Assert.Equal("access-token", payload!.AccessToken);
        Assert.Equal("refresh-token", payload.RefreshToken);
        Assert.True(payload.ExpiresIn > 0);

        // Sanity: the caller (HR.Web) receives these as data, never as something already embedded
        // in a browser-visible URL.
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("://", raw);
    }

    [Fact]
    public async Task Post_Login_Returns_BadRequest_With_Generic_Message_For_Invalid_Credentials()
    {
        _factory.SupabaseAuthGateway.ShouldThrowOnSignIn = true;

        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/login", new { email = "nobody@example.com", password = "wrong-password" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("Invalid email or password.", doc.RootElement.GetProperty("error").GetString());
        Assert.DoesNotContain("wrong-password", body);
    }

    [Theory]
    [InlineData("not-an-email", "P@ssw0rd123")]
    [InlineData("", "P@ssw0rd123")]
    [InlineData("ada@example.com", "")]
    public async Task Post_Login_Returns_UnprocessableEntity_For_Invalid_Request(string email, string password)
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/login", new { email, password });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_Login_Does_Not_Require_Authentication()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/login", new { email = "ada@example.com", password = "P@ssw0rd123" });

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed record LoginPayload(string AccessToken, string RefreshToken, int ExpiresIn);
}
