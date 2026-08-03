using System.Net;
using System.Text.Json;
using HR.Integration.Tests.Infrastructure;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class BackgroundJobDiagnosticsTests(ApiWebApplicationFactory factory)
{
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip
    };

    [Fact]
    public async Task BackgroundJobs_Endpoint_Is_Reachable()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/background-jobs");

        // Any 2xx or 503 is valid — 500 would indicate the handler itself crashed.
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task BackgroundJobs_Endpoint_Returns_Valid_Json()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/background-jobs");
        var body = await response.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(body, JsonOptions);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
    }

    [Fact]
    public async Task BackgroundJobs_Response_Contains_Required_Fields()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/background-jobs");
        var body = await response.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(body, JsonOptions);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("status", out _), "Response must contain 'status'");
        Assert.True(root.TryGetProperty("servers", out var servers), "Response must contain 'servers'");
        Assert.True(root.TryGetProperty("queues", out var queues), "Response must contain 'queues'");
        Assert.True(root.TryGetProperty("statistics", out _), "Response must contain 'statistics'");
        Assert.True(root.TryGetProperty("checkedAt", out _), "Response must contain 'checkedAt'");

        Assert.Equal(JsonValueKind.Array, servers.ValueKind);
        Assert.Equal(JsonValueKind.Array, queues.ValueKind);
    }

    [Fact]
    public async Task BackgroundJobs_Statistics_Contains_Required_Fields()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/background-jobs");
        var body = await response.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(body, JsonOptions);
        var stats = doc.RootElement.GetProperty("statistics");

        Assert.True(stats.TryGetProperty("enqueued", out _));
        Assert.True(stats.TryGetProperty("processing", out _));
        Assert.True(stats.TryGetProperty("scheduled", out _));
        Assert.True(stats.TryGetProperty("failed", out _));
        Assert.True(stats.TryGetProperty("succeeded", out _));
        Assert.True(stats.TryGetProperty("recurring", out _));
    }

    [Fact]
    public async Task BackgroundJobs_Has_Zero_Failed_Jobs_On_Fresh_Database()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/background-jobs");
        var body = await response.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(body, JsonOptions);
        var failed = doc.RootElement
            .GetProperty("statistics")
            .GetProperty("failed")
            .GetInt64();

        Assert.Equal(0, failed);
    }

    [Fact]
    public async Task BackgroundJobs_Status_Is_Not_Degraded_On_Fresh_Database()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/background-jobs");
        var body = await response.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(body, JsonOptions);
        var status = doc.RootElement.GetProperty("status").GetString();

        // Fresh database has no failed jobs so status must not be "degraded".
        Assert.NotEqual("degraded", status);
    }

    [Fact]
    public async Task BackgroundJobs_CheckedAt_Is_A_Recent_Timestamp()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-5);

        using var client = factory.CreateClient();
        var response = await client.GetAsync("/health/background-jobs");
        var body = await response.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(body, JsonOptions);
        var checkedAt = doc.RootElement
            .GetProperty("checkedAt")
            .GetDateTimeOffset();

        Assert.True(checkedAt >= before, $"checkedAt {checkedAt} should be after request started at {before}");
        Assert.True(checkedAt <= DateTimeOffset.UtcNow.AddSeconds(5));
    }
}
