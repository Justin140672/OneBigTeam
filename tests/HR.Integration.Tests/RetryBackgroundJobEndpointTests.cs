using System.Net;
using System.Net.Http.Json;

using HR.Integration.Tests.Infrastructure;

namespace HR.Integration.Tests;

/// <summary>
/// See ForceCustomerReadOnlyEndpointTests for the shared platform-admin allow-list test pattern
/// this class follows. Exercises the real HangfireJobStatusReader against the test harness's real
/// (test) Hangfire storage — no fake IBackgroundJobStatusReader is registered.
///
/// A full happy-path (200 + job transitions out of Failed state) is not covered here: doing so
/// would require actually enqueuing a Hangfire job and forcing a worker to fail it within this
/// test harness, and no existing integration test in this project does that (BackgroundJobDiagnosticsTests
/// only asserts a fresh database has zero failed jobs). Fabricating a failed-job row directly against
/// Hangfire's storage tables would not reflect real Hangfire behaviour, so this is left as a gap.
/// </summary>
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

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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
