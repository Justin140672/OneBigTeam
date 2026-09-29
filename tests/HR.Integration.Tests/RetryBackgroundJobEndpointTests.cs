using System.Net;
using System.Net.Http.Json;

using HR.Integration.Tests.Infrastructure;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class RetryBackgroundJobEndpointTests
{
    private const string AllowListedEmail = "priya.shah@acme.example";

    private readonly ApiWebApplicationFactory _factory;

    public RetryBackgroundJobEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient ClientFor(Guid userId, string? email)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        if (!string.IsNullOrWhiteSpace(email))
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.EmailHeader, email);
        }

        return client;
    }

    private static string Url(string jobId) => $"/api/companies/admin/background-jobs/{jobId}/retry";

    [Fact]
    public async Task Post_Retry_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            Url("some-job-id"), new { reason = "Investigating a transient failure before retrying." });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_Retry_Returns_Unauthorized_For_Authenticated_Caller_Not_On_AllowList()
    {
        using var client = ClientFor(Guid.NewGuid(), "not-allow-listed@example.com");

        var response = await client.PostAsJsonAsync(
            Url("some-job-id"), new { reason = "Investigating a transient failure before retrying." });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_Retry_Returns_NotFound_For_Unknown_JobId()
    {
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.PostAsJsonAsync(
            Url($"nonexistent-job-{Guid.NewGuid():N}"),
            new { reason = "Investigating a transient failure before retrying." });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_Retry_Returns_UnprocessableEntity_When_Reason_Is_Too_Short()
    {
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.PostAsJsonAsync(Url("some-job-id"), new { reason = "abcd" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_Retry_Returns_UnprocessableEntity_When_Reason_Is_Empty()
    {
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.PostAsJsonAsync(Url("some-job-id"), new { reason = "" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}
