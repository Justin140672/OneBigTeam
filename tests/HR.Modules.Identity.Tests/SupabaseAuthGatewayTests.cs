using System.Net;
using HR.Modules.Identity.Services;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace HR.Modules.Identity.Tests;

public class SupabaseAuthGatewayTests
{
    private static SupabaseAuthOptions Options() => new()
    {
        ProjectUrl = "https://example.supabase.co",
        PublishableKey = "publishable-key",
        SecretKey = "secret-key",
        JwksUrl = "https://example.supabase.co/auth/v1/.well-known/jwks.json",
    };

    private static SupabaseAuthGateway BuildGateway(FakeHttpMessageHandler handler, SupabaseAuthOptions? options = null) =>
        new(new FakeHttpClientFactory(handler), Microsoft.Extensions.Options.Options.Create(options ?? Options()));

    [Fact]
    public async Task CreateUserAsync_Returns_ParsedGuid_On_Success()
    {
        var userId = Guid.NewGuid();
        var handler = new FakeHttpMessageHandler
        {
            StatusCodeToReturn = HttpStatusCode.OK,
            ResponseBodyToReturn = $$"""{"id": "{{userId}}"}""",
        };
        var options = Options();
        var gateway = BuildGateway(handler, options);

        var result = await gateway.CreateUserAsync("ada@example.com", "s3cret!!", "https://app.example.com/verify-email", CancellationToken.None);

        Assert.Equal(userId, result);
        Assert.Equal(2, handler.Requests.Count);

        // Step 1: admin-create the user with the real password baked in from the start (not
        // /auth/v1/invite — see CreateUserAsync's remarks on why that combination doesn't produce
        // a working password against real Supabase).
        var (createRequest, createBody) = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, createRequest.Method);
        Assert.Equal("https://example.supabase.co/auth/v1/admin/users", createRequest.RequestUri!.ToString());
        Assert.Contains("s3cret!!", createBody);
        Assert.True(createRequest.Headers.Contains("apikey"));
        Assert.Equal(options.SecretKey, createRequest.Headers.GetValues("apikey").Single());
        Assert.Equal(options.SecretKey, createRequest.Headers.Authorization?.Parameter);

        // Step 2: the confirmation email is sent via a separate /auth/v1/resend call, since the
        // admin-create endpoint above never sends one itself.
        var (resendRequest, resendBody) = handler.Requests[1];
        Assert.Equal(HttpMethod.Post, resendRequest.Method);
        Assert.Equal("https://example.supabase.co/auth/v1/resend", resendRequest.RequestUri!.ToString());
        Assert.Contains("ada@example.com", resendBody);
    }

    [Fact]
    public async Task CreateUserAsync_Throws_With_ResponseBody_On_Failure()
    {
        var handler = new FakeHttpMessageHandler
        {
            StatusCodeToReturn = HttpStatusCode.BadRequest,
            ResponseBodyToReturn = "{\"error\": \"invalid email address\"}",
        };
        var gateway = BuildGateway(handler);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => gateway.CreateUserAsync("ada@example.com", "s3cret!!", "https://app.example.com/verify-email", CancellationToken.None));

        Assert.Contains("invalid email address", ex.Message);
    }

    [Fact]
    public async Task CreateUserAsync_Throws_EmailAlreadyRegisteredException_When_Supabase_Reports_Duplicate()
    {
        var handler = new FakeHttpMessageHandler
        {
            StatusCodeToReturn = HttpStatusCode.UnprocessableEntity,
            ResponseBodyToReturn = "{\"error_code\": \"email_exists\"}",
        };
        var gateway = BuildGateway(handler);

        await Assert.ThrowsAsync<EmailAlreadyRegisteredException>(
            () => gateway.CreateUserAsync("ada@example.com", "s3cret!!", "https://app.example.com/verify-email", CancellationToken.None));
    }

    [Fact]
    public async Task ExchangeCodeForSessionAsync_Returns_PopulatedSession_On_Success()
    {
        var userId = Guid.NewGuid();
        var handler = new FakeHttpMessageHandler
        {
            StatusCodeToReturn = HttpStatusCode.OK,
            ResponseBodyToReturn = $$"""
                {
                    "access_token": "access-token-value",
                    "refresh_token": "refresh-token-value",
                    "expires_in": 3600,
                    "user": { "id": "{{userId}}" }
                }
                """,
        };
        var options = Options();
        var gateway = BuildGateway(handler, options);

        var before = DateTimeOffset.UtcNow;
        var session = await gateway.ExchangeCodeForSessionAsync("some-code", CancellationToken.None);

        Assert.Equal("access-token-value", session.AccessToken);
        Assert.Equal("refresh-token-value", session.RefreshToken);
        Assert.Equal(userId, session.UserId);
        Assert.True(session.ExpiresAt > before);

        Assert.NotNull(handler.LastRequest);
        Assert.True(handler.LastRequest!.Headers.Contains("apikey"));
        Assert.Equal(options.PublishableKey, handler.LastRequest.Headers.GetValues("apikey").Single());
        Assert.NotEqual(options.SecretKey, handler.LastRequest.Headers.GetValues("apikey").Single());
    }

    [Fact]
    public async Task ExchangeCodeForSessionAsync_Throws_With_ResponseBody_On_Failure()
    {
        var handler = new FakeHttpMessageHandler
        {
            StatusCodeToReturn = HttpStatusCode.Unauthorized,
            ResponseBodyToReturn = "{\"error\": \"invalid code\"}",
        };
        var gateway = BuildGateway(handler);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => gateway.ExchangeCodeForSessionAsync("bad-code", CancellationToken.None));

        Assert.Contains("invalid code", ex.Message);
    }
}
