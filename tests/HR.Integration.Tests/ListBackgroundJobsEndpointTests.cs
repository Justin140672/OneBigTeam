using System.Net;
using System.Text.Json;

using HR.Integration.Tests.Infrastructure;

namespace HR.Integration.Tests;

/// <summary>
/// See ForceCustomerReadOnlyEndpointTests for the shared platform-admin allow-list test pattern
/// this class follows. Exercises the real HangfireJobStatusReader against the test harness's real
/// (test) Hangfire storage — no fake IBackgroundJobStatusReader is registered, mirroring
/// BackgroundJobDiagnosticsTests.
/// </summary>
[Collection("Integration")]
public class ListBackgroundJobsEndpointTests
{
    private const string AllowListedEmail = "priya.shah@acme.example";
    private const string Url = "/api/companies/admin/background-jobs";

    private readonly ApiWebApplicationFactory _factory;

    public ListBackgroundJobsEndpointTests(ApiWebApplicationFactory factory)
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

    [Fact]
    public async Task Get_BackgroundJobs_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_BackgroundJobs_Returns_Unauthorized_For_Authenticated_Caller_Not_On_AllowList()
    {
        using var client = ClientFor(Guid.NewGuid(), "not-allow-listed@example.com");

        var response = await client.GetAsync(Url);

        // See PlatformAdminAuthorizationHandler.cs / f2658d7d — authenticated-but-not-authorized
        // is Forbidden (403), not Unauthorized (401).
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_BackgroundJobs_Returns_Ok_With_Expected_Shape_For_AllowListed_Caller()
    {
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.GetAsync(Url);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("available", out _), "Response must contain 'available'");
        Assert.True(root.TryGetProperty("scheduled", out var scheduled), "Response must contain 'scheduled'");
        Assert.True(root.TryGetProperty("running", out var running), "Response must contain 'running'");
        Assert.True(root.TryGetProperty("failed", out var failed), "Response must contain 'failed'");

        Assert.Equal(JsonValueKind.Array, scheduled.ValueKind);
        Assert.Equal(JsonValueKind.Array, running.ValueKind);
        Assert.Equal(JsonValueKind.Array, failed.ValueKind);
    }
}
