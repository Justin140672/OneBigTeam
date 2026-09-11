using System.Net;

using HR.Modules.Identity.Services;
using HR.Modules.Identity.Tests.Infrastructure;

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace HR.Modules.Identity.Tests;

public class SupabaseAuthHealthCheckTests
{
    private static SupabaseAuthOptions Options() => new()
    {
        ProjectUrl = "https://example.supabase.co",
        PublishableKey = "publishable-key",
        SecretKey = "secret-key",
        JwksUrl = "https://example.supabase.co/auth/v1/.well-known/jwks.json",
    };

    private static SupabaseAuthHealthCheck BuildHealthCheck(FakeHttpMessageHandler handler, SupabaseAuthOptions? options = null) =>
        new(new FakeHttpClientFactory(handler), Microsoft.Extensions.Options.Options.Create(options ?? new SupabaseAuthOptions()));

    [Fact]
    public async Task CheckHealthAsync_Returns_Degraded_When_ProjectUrl_Not_Configured()
    {
        var handler = new FakeHttpMessageHandler();
        var healthCheck = BuildHealthCheck(handler, new SupabaseAuthOptions { ProjectUrl = "" });

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Equal("Supabase Auth is not configured.", result.Description);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task CheckHealthAsync_Returns_Healthy_When_Configured_And_Reachable()
    {
        var handler = new FakeHttpMessageHandler
        {
            StatusCodeToReturn = HttpStatusCode.OK,
            ResponseBodyToReturn = "{}",
        };
        var healthCheck = BuildHealthCheck(handler, Options());

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal("https://example.supabase.co/auth/v1/settings", handler.LastRequest!.RequestUri!.ToString());
        Assert.True(handler.LastRequest.Headers.TryGetValues("apikey", out var apiKey));
        Assert.Equal("publishable-key", Assert.Single(apiKey!));
        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization!.Scheme);
        Assert.Equal("publishable-key", handler.LastRequest.Headers.Authorization!.Parameter);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task CheckHealthAsync_Returns_Healthy_When_Endpoint_Returns_Auth_Level_Response(HttpStatusCode code)
    {
        var handler = new FakeHttpMessageHandler { StatusCodeToReturn = code, ResponseBodyToReturn = "{}" };
        var healthCheck = BuildHealthCheck(handler, Options());

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_Returns_Degraded_When_PublishableKey_Missing()
    {
        var handler = new FakeHttpMessageHandler();
        var healthCheck = BuildHealthCheck(handler, new SupabaseAuthOptions
        {
            ProjectUrl = "https://example.supabase.co",
            PublishableKey = "",
        });

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task CheckHealthAsync_Returns_Unhealthy_When_Transport_Fails()
    {
        var handler = new FakeHttpMessageHandler { ExceptionToThrow = new HttpRequestException("boom") };
        var healthCheck = BuildHealthCheck(handler, Options());

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_Returns_Unhealthy_When_Configured_But_Endpoint_Returns_Error()
    {
        var handler = new FakeHttpMessageHandler
        {
            StatusCodeToReturn = HttpStatusCode.InternalServerError,
            ResponseBodyToReturn = "{}",
        };
        var healthCheck = BuildHealthCheck(handler, Options());

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("500", result.Description);
    }
}
