using System.Net;
using System.Net.Http.Json;

using HR.Integration.Tests.Infrastructure;

namespace HR.Integration.Tests;

/// <summary>
/// Same "platform:admin" policy + allow-list gate pattern as GetSystemHealthEndpointTests /
/// ListBackgroundJobsEndpointTests — see their remarks. Runs against the test host's real
/// IPlatformDocumentActivityReader (HR.Modules.Documents) and IPlatformUserActivityReader
/// (HR.Modules.Identity) implementations, so this only asserts structural correctness (30-point
/// zero-or-more series, non-negative current values) rather than specific counts, since the test
/// database's seeded state isn't asserted here.
/// </summary>
[Collection("Integration")]
public class GetApplicationMetricsEndpointTests
{
    private const string AllowListedEmail = "priya.shah@acme.example";
    private const string Url = "/api/companies/admin/application-metrics";

    private readonly ApiWebApplicationFactory _factory;

    public GetApplicationMetricsEndpointTests(ApiWebApplicationFactory factory)
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
    public async Task Get_ApplicationMetrics_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_ApplicationMetrics_Returns_Unauthorized_For_Authenticated_Caller_Not_On_AllowList()
    {
        using var client = ClientFor(Guid.NewGuid(), "not-allow-listed@example.com");

        var response = await client.GetAsync(Url);

        // See PlatformAdminAuthorizationHandler.cs / f2658d7d — authenticated-but-not-authorized
        // is Forbidden (403), not Unauthorized (401).
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_ApplicationMetrics_Returns_Ok_With_WellFormed_Payload_For_AllowListed_Caller()
    {
        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.GetAsync(Url);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ApplicationMetricsPayload>();
        Assert.NotNull(payload);

        Assert.Equal(30, payload!.DailySignups.Count);
        Assert.Equal(30, payload.DailyDocumentsUploaded.Count);
        Assert.All(payload.DailySignups, point => Assert.True(point.Count >= 0));
        Assert.All(payload.DailyDocumentsUploaded, point => Assert.True(point.Count >= 0));

        Assert.NotNull(payload.ActiveCompaniesTrend);

        Assert.True(payload.CurrentActiveCompanies >= 0);
        Assert.True(payload.CurrentActiveUsers >= 0);
        Assert.True(payload.CurrentStorageConsumedBytes >= 0);
        Assert.True(payload.CurrentBackgroundJobsSucceededTotal >= 0);

        Assert.False(payload.EmailsSentTracked);
        Assert.False(string.IsNullOrWhiteSpace(payload.EmailsSentGapReason));
    }

    private sealed record DailyMetricPointPayload(DateOnly Date, int Count);

    private sealed record ApplicationMetricsPayload(
        IReadOnlyList<DailyMetricPointPayload> DailySignups,
        IReadOnlyList<DailyMetricPointPayload> DailyDocumentsUploaded,
        IReadOnlyList<DailyMetricPointPayload> ActiveCompaniesTrend,
        int CurrentActiveCompanies,
        int CurrentActiveUsers,
        long CurrentStorageConsumedBytes,
        int CurrentBackgroundJobsSucceededTotal,
        bool EmailsSentTracked,
        string EmailsSentGapReason);
}
