using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class ResendVerificationEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public ResendVerificationEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.SupabaseAuthGateway.Reset();
    }

    private static object ValidSignUpRequest(string email) => new
    {
        companyName = $"Acme-{Guid.NewGuid():N}",
        adminFirstName = "Ada",
        adminLastName = "Lovelace",
        adminEmail = email,
        password = "P@ssw0rd123",
    };

    [Fact]
    public async Task Post_ResendVerification_Does_Not_Return_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/resend-verification", new { email = $"missing-{Guid.NewGuid():N}@example.com" });

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_ResendVerification_Returns_Success_And_Calls_Gateway_When_Email_Exists()
    {
        using var client = _factory.CreateClient();
        var email = $"ada-{Guid.NewGuid():N}@example.com";

        var signUpResponse = await client.PostAsJsonAsync("/api/signup", ValidSignUpRequest(email));
        Assert.Equal(HttpStatusCode.OK, signUpResponse.StatusCode);

        // The signup itself already created a Supabase user via the gateway — reset the fake so
        // this test only observes the ResendVerificationEmailAsync call, not CreateUserAsync.
        _factory.SupabaseAuthGateway.ResentEmails.Clear();

        var response = await client.PostAsJsonAsync("/api/resend-verification", new { email });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ResendVerificationPayload>();
        Assert.NotNull(payload);
        Assert.True(payload!.Success);

        Assert.Contains(_factory.SupabaseAuthGateway.ResentEmails, r => r.Email == email);
    }

    [Fact]
    public async Task Post_ResendVerification_Returns_Success_Without_Calling_Gateway_When_Email_Does_Not_Exist()
    {
        using var client = _factory.CreateClient();
        var email = $"missing-{Guid.NewGuid():N}@example.com";

        var response = await client.PostAsJsonAsync("/api/resend-verification", new { email });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ResendVerificationPayload>();
        Assert.NotNull(payload);
        Assert.True(payload!.Success);

        Assert.DoesNotContain(_factory.SupabaseAuthGateway.ResentEmails, r => r.Email == email);
    }

    [Fact]
    public async Task Post_ResendVerification_Returns_BadRequest_When_Email_Is_Invalid()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/resend-verification", new { email = "not-an-email" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private sealed record ResendVerificationPayload(bool Success);
}
