using System.Net;

using HR.Infrastructure.Storage;
using HR.Infrastructure.Tests.Infrastructure;

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

using Xunit;

namespace HR.Infrastructure.Tests.Storage;

public class SupabaseStorageHealthCheckTests
{
    private static SupabaseProfilePhotoStorageOptions Options() => new()
    {
        SupabaseUrl = "https://example.supabase.co",
        ServiceRoleKey = "service-role-key",
        BucketName = "profile-photos",
        SignedUrlExpirySeconds = 3600,
    };

    private static SupabaseStorageHealthCheck BuildHealthCheck(
        FakeHttpMessageHandler handler, SupabaseProfilePhotoStorageOptions? options = null) =>
        new(new FakeHttpClientFactory(handler), Microsoft.Extensions.Options.Options.Create(options ?? Options()));

    [Fact]
    public async Task Reachable_With_Bucket_Present_Returns_Healthy()
    {
        var handler = new FakeHttpMessageHandler
        {
            StatusCodeToReturn = HttpStatusCode.OK,
            ResponseBodyToReturn = """[{"id":"profile-photos","name":"profile-photos"}]""",
        };

        var result = await BuildHealthCheck(handler).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task Reachable_But_Bucket_Missing_Returns_Unhealthy()
    {
        var handler = new FakeHttpMessageHandler
        {
            StatusCodeToReturn = HttpStatusCode.OK,
            ResponseBodyToReturn = """[{"id":"some-other-bucket"}]""",
        };

        var result = await BuildHealthCheck(handler).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("bucket", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Authentication_Failure_Returns_Unhealthy()
    {
        var handler = new FakeHttpMessageHandler { StatusCodeToReturn = HttpStatusCode.Unauthorized };

        var result = await BuildHealthCheck(handler).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task Malformed_Response_Body_Returns_Unhealthy()
    {
        var handler = new FakeHttpMessageHandler
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
        var handler = new FakeHttpMessageHandler { ExceptionToThrow = new HttpRequestException("boom") };

        var result = await BuildHealthCheck(handler).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task Cancelled_Token_Surfaced_By_Handler_Results_In_Unhealthy()
    {
        var handler = new FakeHttpMessageHandler { ExceptionToThrow = new OperationCanceledException() };

        var result = await BuildHealthCheck(handler).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task Not_Configured_Returns_Degraded()
    {
        var handler = new FakeHttpMessageHandler();
        var options = Options();
        options.SupabaseUrl = "";

        var result = await BuildHealthCheck(handler, options).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Null(handler.LastRequest);
    }
}
