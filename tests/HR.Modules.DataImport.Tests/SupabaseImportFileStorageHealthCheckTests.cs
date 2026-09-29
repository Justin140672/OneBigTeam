using System.Net;

using HR.Modules.DataImport.Services;
using HR.Modules.DataImport.Tests.Infrastructure;

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

using Xunit;

namespace HR.Modules.DataImport.Tests;

public class SupabaseImportFileStorageHealthCheckTests
{
    private static SupabaseImportFileStorageOptions Options() => new()
    {
        SupabaseUrl = "https://example.supabase.co",
        ServiceRoleKey = "service-role-key",
        BucketName = "import-files",
        SignedUrlExpirySeconds = 3600,
    };

    private static SupabaseImportFileStorageHealthCheck BuildHealthCheck(
        FakeHttpMessageHandler handler, SupabaseImportFileStorageOptions? options = null) =>
        new(new FakeHttpClientFactory(handler), Microsoft.Extensions.Options.Options.Create(options ?? Options()));

    [Fact]
    public async Task Reachable_With_Bucket_Present_Returns_Healthy()
    {
        var handler = new FakeHttpMessageHandler
        {
            StatusCodeToReturn = HttpStatusCode.OK,
            ResponseBodyToReturn = """[{"id":"import-files"}]""",
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
            ResponseBodyToReturn = """[{"id":"other-bucket"}]""",
        };

        var result = await BuildHealthCheck(handler).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
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
    public async Task Not_Configured_Returns_Unhealthy()
    {
        var handler = new FakeHttpMessageHandler();
        var options = Options();
        options.ServiceRoleKey = "";

        var result = await BuildHealthCheck(handler, options).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Null(handler.LastRequest);
    }
}
