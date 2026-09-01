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
    public async Task GenerateRecoveryLinkAsync_Returns_ActionLink_And_Uses_SecretKey()
    {
        var handler = new FakeHttpMessageHandler
        {
            StatusCodeToReturn = HttpStatusCode.OK,
            ResponseBodyToReturn = """{"action_link": "https://example.supabase.co/auth/v1/verify?token=abc&type=recovery&redirect_to=https://app/reset-password"}""",
        };
        var options = Options();
        var gateway = BuildGateway(handler, options);

        var link = await gateway.GenerateRecoveryLinkAsync(
            "ada@example.com", "https://app/reset-password", CancellationToken.None);

        Assert.Equal(
            "https://example.supabase.co/auth/v1/verify?token=abc&type=recovery&redirect_to=https://app/reset-password",
            link);

        var (request, body) = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://example.supabase.co/auth/v1/admin/generate_link", request.RequestUri!.ToString());
        Assert.Contains("recovery", body);
        Assert.Contains("ada@example.com", body);
        Assert.Equal(options.SecretKey, request.Headers.GetValues("apikey").Single());
    }

    [Fact]
    public async Task GenerateRecoveryLinkAsync_Throws_With_ResponseBody_On_Failure()
    {
        var handler = new FakeHttpMessageHandler
        {
            StatusCodeToReturn = HttpStatusCode.BadRequest,
            ResponseBodyToReturn = "{\"error\": \"user not found\"}",
        };
        var gateway = BuildGateway(handler);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => gateway.GenerateRecoveryLinkAsync("nobody@example.com", "https://app/reset-password", CancellationToken.None));

        Assert.Contains("user not found", ex.Message);
    }

    [Fact]
    public async Task GenerateRecoveryLinkAsync_Throws_When_ActionLink_Missing()
    {
        var handler = new FakeHttpMessageHandler
        {
            StatusCodeToReturn = HttpStatusCode.OK,
            ResponseBodyToReturn = "{\"hashed_token\": \"abc\"}",
        };
        var gateway = BuildGateway(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => gateway.GenerateRecoveryLinkAsync("ada@example.com", "https://app/reset-password", CancellationToken.None));
    }

    [Fact]
    public async Task SignInWithPasswordAsync_Failure_Message_Redacts_Tokens_But_Keeps_Error_Reason()
    {
        var handler = new FakeHttpMessageHandler
        {
            StatusCodeToReturn = HttpStatusCode.BadRequest,
            ResponseBodyToReturn =
                """
                {
                    "access_token": "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.super-secret-access",
                    "refresh_token": "v1.MrefreshTokenValue0xdeadbeef",
                    "error": "invalid_grant",
                    "error_description": "Invalid login credentials"
                }
                """,
        };
        var gateway = BuildGateway(handler);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => gateway.SignInWithPasswordAsync("ada@example.com", "bad-password", CancellationToken.None));

        Assert.Contains("invalid_grant", ex.Message);
        Assert.Contains("Invalid login credentials", ex.Message);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiJ9", ex.Message);
        Assert.DoesNotContain("super-secret-access", ex.Message);
        Assert.DoesNotContain("MrefreshTokenValue", ex.Message);
        Assert.DoesNotContain("access_token", ex.Message);
    }

    [Fact]
    public async Task GenerateRecoveryLinkAsync_Failure_Message_Does_Not_Leak_Action_Link_Or_Token()
    {
        var handler = new FakeHttpMessageHandler
        {
            StatusCodeToReturn = HttpStatusCode.BadRequest,
            ResponseBodyToReturn =
                """{"hashed_token": "pkce_9f8e7d6c5b4a3210", "action_link": "https://proj.supabase.co/auth/v1/verify?token=pkce_9f8e7d6c5b4a3210&type=recovery", "error": "over_email_send_rate_limit"}""",
        };
        var gateway = BuildGateway(handler);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => gateway.GenerateRecoveryLinkAsync("ada@example.com", "https://app/reset-password", CancellationToken.None));

        Assert.Contains("over_email_send_rate_limit", ex.Message);
        Assert.DoesNotContain("pkce_9f8e7d6c5b4a3210", ex.Message);
        Assert.DoesNotContain("action_link", ex.Message);
        Assert.DoesNotContain("hashed_token", ex.Message);
    }

    [Fact]
    public async Task UpdatePasswordAsync_Failure_Message_Redacts_Tokens()
    {
        var handler = new FakeHttpMessageHandler
        {
            StatusCodeToReturn = HttpStatusCode.Unauthorized,
            ResponseBodyToReturn =
                """{"access_token": "eyJhbGciOiJIUzI1NiJ9.payload.signature", "msg": "token has expired or is invalid"}""",
        };
        var gateway = BuildGateway(handler);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => gateway.UpdatePasswordAsync("user-access-token", "NewPassw0rd!", CancellationToken.None));

        Assert.Contains("token has expired or is invalid", ex.Message);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiJ9", ex.Message);
        Assert.DoesNotContain("access_token", ex.Message);
    }

    [Fact]
    public async Task SignOutAsync_Posts_To_GoTrue_Logout_Global_With_The_User_Bearer()
    {
        var handler = new FakeHttpMessageHandler
        {
            StatusCodeToReturn = HttpStatusCode.NoContent,
            ResponseBodyToReturn = string.Empty,
        };
        var options = Options();
        var gateway = BuildGateway(handler, options);

        await gateway.SignOutAsync("user-access-token", CancellationToken.None);

        var (request, _) = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://example.supabase.co/auth/v1/logout?scope=global", request.RequestUri!.ToString());
        // apikey stays the publishable key; Authorization carries the user's own token so GoTrue
        // knows whose sessions to revoke.
        Assert.Equal(options.PublishableKey, request.Headers.GetValues("apikey").Single());
        Assert.Equal("user-access-token", request.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task SignOutAsync_Throws_On_Failure_Without_Leaking_The_Token()
    {
        var handler = new FakeHttpMessageHandler
        {
            StatusCodeToReturn = HttpStatusCode.InternalServerError,
            ResponseBodyToReturn = """{"access_token": "eyJhbGciOiJIUzI1NiJ9.leaky.token", "msg": "boom"}""",
        };
        var gateway = BuildGateway(handler);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => gateway.SignOutAsync("user-access-token", CancellationToken.None));

        Assert.Contains("500", ex.Message);
        Assert.DoesNotContain("user-access-token", ex.Message);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiJ9", ex.Message);
        Assert.DoesNotContain("access_token", ex.Message);
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
