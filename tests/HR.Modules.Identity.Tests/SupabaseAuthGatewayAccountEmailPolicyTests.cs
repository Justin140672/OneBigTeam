using System.Net;
using HR.Modules.Identity.Services;
using HR.Modules.Identity.Services.AccountEmailPolicy;
using HR.Modules.Identity.Tests.Infrastructure;

namespace HR.Modules.Identity.Tests;

/// <summary>
/// Ticket 9: the Supabase gateway is the last line of defence for the account-creation
/// email-domain policy — every method that creates a NEW identity-provider account refuses a
/// public/disposable (or malformed) address BEFORE issuing any HTTP request, while sign-in,
/// password reset and lookups for existing accounts on those domains are left untouched.
/// </summary>
public class SupabaseAuthGatewayAccountEmailPolicyTests
{
    private static SupabaseAuthOptions Options() => new()
    {
        ProjectUrl = "https://example.supabase.co",
        PublishableKey = "publishable-key",
        SecretKey = "secret-key",
        JwksUrl = "https://example.supabase.co/auth/v1/.well-known/jwks.json",
    };

    private static SupabaseAuthGateway BuildGateway(
        FakeHttpMessageHandler handler, IAccountEmailDomainPolicy? policy = null) =>
        new(new FakeHttpClientFactory(handler), Microsoft.Extensions.Options.Options.Create(Options()), policy);

    private static FakeHttpMessageHandler UserCreatedHandler() => new()
    {
        StatusCodeToReturn = HttpStatusCode.OK,
        ResponseBodyToReturn = $$"""{"id": "{{Guid.NewGuid()}}"}""",
    };


    [Theory]
    [InlineData("person@gmail.com")]
    [InlineData("person@GMAIL.COM")]
    [InlineData("person@mx.mailinator.com")]
    public async Task CreateUserAsync_Throws_For_Public_Domain_Without_Any_Http_Request(string email)
    {
        var handler = UserCreatedHandler();
        var gateway = BuildGateway(handler);

        await Assert.ThrowsAsync<AccountEmailDomainNotPermittedException>(
            () => gateway.CreateUserAsync(email, "s3cret!!", "https://app.example.com/verify-email", CancellationToken.None));

        Assert.Empty(handler.Requests);
        Assert.Null(handler.LastRequest);
    }

    [Theory]
    [InlineData("person@hotmail.com")]
    [InlineData("person@yahoo.co.uk")]
    public async Task CreateConfirmedUserAsync_Throws_For_Public_Domain_Without_Any_Http_Request(string email)
    {
        var handler = UserCreatedHandler();
        var gateway = BuildGateway(handler);

        await Assert.ThrowsAsync<AccountEmailDomainNotPermittedException>(
            () => gateway.CreateConfirmedUserAsync(email, "s3cret!!", CancellationToken.None));

        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("person@outlook.com")]
    [InlineData("person@proton.me")]
    public async Task CreatePendingUserWithMetadataAsync_Throws_For_Public_Domain_Without_Any_Http_Request(string email)
    {
        var handler = UserCreatedHandler();
        var gateway = BuildGateway(handler);

        await Assert.ThrowsAsync<AccountEmailDomainNotPermittedException>(
            () => gateway.CreatePendingUserWithMetadataAsync(
                email, "https://admin.example.com/welcome",
                new Dictionary<string, string> { ["correlation"] = "x" }, CancellationToken.None));

        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("no-at-sign")]
    [InlineData("person@localhost")]
    [InlineData("")]
    public async Task Create_Methods_Throw_For_Malformed_Address_Without_Any_Http_Request(string email)
    {
        var handler = UserCreatedHandler();
        var gateway = BuildGateway(handler);

        await Assert.ThrowsAsync<AccountEmailDomainNotPermittedException>(
            () => gateway.CreateUserAsync(email, "s3cret!!", "https://app.example.com/verify-email", CancellationToken.None));
        await Assert.ThrowsAsync<AccountEmailDomainNotPermittedException>(
            () => gateway.CreateConfirmedUserAsync(email, "s3cret!!", CancellationToken.None));
        await Assert.ThrowsAsync<AccountEmailDomainNotPermittedException>(
            () => gateway.CreatePendingUserWithMetadataAsync(
                email, "https://admin.example.com/welcome", new Dictionary<string, string>(), CancellationToken.None));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Exception_Message_Names_The_Domain_But_Not_The_Address()
    {
        var gateway = BuildGateway(UserCreatedHandler());

        var ex = await Assert.ThrowsAsync<AccountEmailDomainNotPermittedException>(
            () => gateway.CreateConfirmedUserAsync("janet.secret-local@gmail.com", "s3cret!!", CancellationToken.None));

        Assert.Contains("gmail.com", ex.Message);
        Assert.DoesNotContain("janet.secret-local", ex.Message);
        Assert.IsAssignableFrom<InvalidOperationException>(ex);
    }

    [Fact]
    public async Task Create_Methods_Use_An_Injected_Policy_When_Supplied()
    {
        var handler = UserCreatedHandler();
        var policy = new AccountEmailDomainPolicy(["custom-disposable.test"]);
        var gateway = BuildGateway(handler, policy);

        await Assert.ThrowsAsync<AccountEmailDomainNotPermittedException>(
            () => gateway.CreateConfirmedUserAsync("person@custom-disposable.test", "s3cret!!", CancellationToken.None));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CreateConfirmedUserAsync_Organisation_Domain_Still_Issues_The_Request()
    {
        var userId = Guid.NewGuid();
        var handler = new FakeHttpMessageHandler
        {
            StatusCodeToReturn = HttpStatusCode.OK,
            ResponseBodyToReturn = $$"""{"id": "{{userId}}"}""",
        };
        var gateway = BuildGateway(handler);

        var result = await gateway.CreateConfirmedUserAsync("person@brightsparks-consulting.co.uk", "s3cret!!", CancellationToken.None);

        Assert.Equal(userId, result);
        Assert.NotEmpty(handler.Requests);
        Assert.Equal("https://example.supabase.co/auth/v1/admin/users", handler.Requests[0].Request.RequestUri!.ToString());
    }


    [Fact]
    public async Task SignInWithPasswordAsync_For_Public_Domain_Still_Issues_Its_Request()
    {
        var userId = Guid.NewGuid();
        var handler = new FakeHttpMessageHandler
        {
            StatusCodeToReturn = HttpStatusCode.OK,
            ResponseBodyToReturn = $$"""{"access_token":"a","refresh_token":"r","expires_in":3600,"user":{"id":"{{userId}}"} }""",
        };
        var gateway = BuildGateway(handler);

        var session = await gateway.SignInWithPasswordAsync("existing.user@gmail.com", "s3cret!!", CancellationToken.None);

        Assert.Equal(userId, session.UserId);
        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal("https://example.supabase.co/auth/v1/token?grant_type=password", request.RequestUri!.ToString());
        Assert.Contains("existing.user@gmail.com", body);
    }

    [Fact]
    public async Task GenerateRecoveryLinkAsync_For_Public_Domain_Still_Issues_Its_Request()
    {
        var handler = new FakeHttpMessageHandler
        {
            StatusCodeToReturn = HttpStatusCode.OK,
            ResponseBodyToReturn = """{"action_link": "https://example.supabase.co/auth/v1/verify?token=abc&type=recovery"}""",
        };
        var gateway = BuildGateway(handler);

        var link = await gateway.GenerateRecoveryLinkAsync("existing.user@hotmail.com", "https://app/reset-password", CancellationToken.None);

        Assert.Equal("https://example.supabase.co/auth/v1/verify?token=abc&type=recovery", link);
        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal("https://example.supabase.co/auth/v1/admin/generate_link", request.RequestUri!.ToString());
        Assert.Contains("existing.user@hotmail.com", body);
    }

    [Fact]
    public async Task RequestPasswordResetAsync_For_Public_Domain_Still_Issues_Its_Request()
    {
        var handler = new FakeHttpMessageHandler { StatusCodeToReturn = HttpStatusCode.OK, ResponseBodyToReturn = "{}" };
        var gateway = BuildGateway(handler);

        await gateway.RequestPasswordResetAsync("existing.admin@outlook.com", "https://admin/reset-password", CancellationToken.None);

        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal("https://example.supabase.co/auth/v1/recover", request.RequestUri!.ToString());
        Assert.Contains("existing.admin@outlook.com", body);
    }

    [Fact]
    public async Task ResendVerificationEmailAsync_For_Public_Domain_Still_Issues_Its_Request()
    {
        var handler = new FakeHttpMessageHandler { StatusCodeToReturn = HttpStatusCode.OK, ResponseBodyToReturn = "{}" };
        var gateway = BuildGateway(handler);

        await gateway.ResendVerificationEmailAsync("existing.user@gmail.com", "https://app/verify-email", CancellationToken.None);

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetUserIdByEmailAsync_For_Public_Domain_Still_Issues_Its_Request()
    {
        var userId = Guid.NewGuid();
        var handler = new FakeHttpMessageHandler
        {
            StatusCodeToReturn = HttpStatusCode.OK,
            ResponseBodyToReturn = $$"""{"users":[{"id":"{{userId}}","email":"existing.user@gmail.com"}]}""",
        };
        var gateway = BuildGateway(handler);

        var resolved = await gateway.GetUserIdByEmailAsync("existing.user@gmail.com", CancellationToken.None);

        Assert.Equal(userId, resolved);
        Assert.Single(handler.Requests);
    }
}
