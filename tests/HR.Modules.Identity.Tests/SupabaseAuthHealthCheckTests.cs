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
