using System.Net;

using HR.Modules.Documents.Services;
using HR.Modules.Documents.Tests.Infrastructure;

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

using Xunit;

namespace HR.Modules.Documents.Tests;

/// <summary>
/// Security review finding 6: <see cref="DocumentStorageHealthCheck"/> must report Unhealthy when
/// the configured bucket is missing from the "list buckets" response, not just when the HTTP call
/// itself fails.
/// </summary>
public class DocumentStorageHealthCheckTests
{
    private static SupabaseStorageOptions Options() => new()
    {
        SupabaseUrl = "https://example.supabase.co",
        ServiceRoleKey = "service-role-key",
        BucketName = "documents",
        SignedUrlExpirySeconds = 3600,
    };

    private static DocumentStorageHealthCheck BuildHealthCheck(
        FakeSupabaseHttpMessageHandler handler, SupabaseStorageOptions? options = null) =>
        new(new FakeHttpClientFactory(handler), Microsoft.Extensions.Options.Options.Create(options ?? Options()));

    [Fact]
    public async Task Reachable_With_Bucket_Present_Returns_Healthy()
    {
        var handler = new FakeSupabaseHttpMessageHandler
        {
            StatusCodeToReturn = HttpStatusCode.OK,
            ResponseBodyToReturn = """[{"id":"documents"}]""",
        };

        var result = await BuildHealthCheck(handler).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task Reachable_But_Bucket_Missing_Returns_Unhealthy()
    {
        var handler = new FakeSupabaseHttpMessageHandler
        {
            StatusCodeToReturn = HttpStatusCode.OK,
            ResponseBodyToReturn = """[{"id":"other-bucket"}]""",
        };

        var result = await BuildHealthCheck(handler).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task Authentication_Failure_Returns_Unhealthy()
    {
        var handler = new FakeSupabaseHttpMessageHandler { StatusCodeToReturn = HttpStatusCode.Unauthorized };

        var result = await BuildHealthCheck(handler).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task Malformed_Response_Body_Returns_Unhealthy()
    {
        var handler = new FakeSupabaseHttpMessageHandler
        {
            StatusCodeToReturn = HttpStatusCode.OK,
            ResponseBodyToReturn = "{not valid json",
        };

        var result = await BuildHealthCheck(handler).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task Transport_Exception_Returns_Unhealthy()
    {
        var handler = new FakeSupabaseHttpMessageHandler { ExceptionToThrow = new HttpRequestException("boom") };

        var result = await BuildHealthCheck(handler).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task Cancelled_Token_Surfaced_By_Handler_Results_In_Unhealthy()
    {
        var handler = new FakeSupabaseHttpMessageHandler { ExceptionToThrow = new OperationCanceledException() };

        var result = await BuildHealthCheck(handler).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task Not_Configured_Returns_Unhealthy()
    {
        var handler = new FakeSupabaseHttpMessageHandler();
        var options = Options();
        options.BucketName = "";

        var result = await BuildHealthCheck(handler, options).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Null(handler.LastRequest);
    }
}
