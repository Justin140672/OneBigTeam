using System.Net;
using System.Text.Json;

using HR.Integration.Tests.Infrastructure;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HR.Integration.Tests;

/// <summary>
/// Security review ticket 6 (P2): <c>/health/background-jobs</c> and
/// <c>/health/startup-migrations</c> both disclosed unauthenticated infrastructure detail
/// (server/queue names, raw exception messages, per-module error text). Both now require the same
/// <c>HealthChecks:ReadinessDetailToken</c> already used by <c>/health/ready</c>
/// (<see cref="Microsoft.Extensions.Hosting.HealthCheckEndpoints"/>) before returning that detail,
/// checked with a constant-time comparison. Uses its own non-Development
/// <see cref="WebApplicationFactory{TEntryPoint}"/> so the token gate is actually exercised
/// (Development always returns detail, same as <c>/health/ready</c>).
/// </summary>
public sealed class DetailedHealthEndpointAuthorizationTests
    : IClassFixture<DetailedHealthEndpointAuthorizationTests.Factory>
{
    private const string DetailToken = "ticket6-test-detail-token";

    private readonly Factory _factory;

    public DetailedHealthEndpointAuthorizationTests(Factory factory) => _factory = factory;

    // ─── /health/background-jobs ───────────────────────────────────────────────

    [Fact]
    public async Task BackgroundJobs_anonymous_request_is_rejected_without_detail()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/background-jobs");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("servers", body);
        Assert.DoesNotContain("queues", body);
        Assert.DoesNotContain("statistics", body);
    }

    [Fact]
    public async Task BackgroundJobs_invalid_token_is_rejected_without_detail()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Health-Token", "wrong-token");

        var response = await client.GetAsync("/health/background-jobs");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("servers", body);
    }

    [Fact]
    public async Task BackgroundJobs_valid_token_returns_full_detail()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Health-Token", DetailToken);

        var response = await client.GetAsync("/health/background-jobs");

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("servers", out _));
        Assert.True(doc.RootElement.TryGetProperty("queues", out _));
        Assert.True(doc.RootElement.TryGetProperty("statistics", out _));
    }

    // ─── /health/startup-migrations ────────────────────────────────────────────

    [Fact]
    public async Task StartupMigrations_anonymous_request_gets_minimal_body_only()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/startup-migrations");

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("status", out _));
        // Per-module keys (e.g. "companies", "identity") and their error detail must not appear.
        Assert.False(doc.RootElement.TryGetProperty("companies", out _));
        Assert.DoesNotContain("checkedAt", body);
    }

    [Fact]
    public async Task StartupMigrations_invalid_token_gets_minimal_body_only()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Health-Token", "wrong-token");

        var response = await client.GetAsync("/health/startup-migrations");

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.False(doc.RootElement.TryGetProperty("companies", out _));
    }

    [Fact]
    public async Task StartupMigrations_valid_token_returns_full_per_module_detail()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Health-Token", DetailToken);

        var response = await client.GetAsync("/health/startup-migrations");

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("companies", out var companies));
        Assert.True(companies.TryGetProperty("status", out _));
        Assert.True(doc.RootElement.TryGetProperty("release", out _));
    }

    [Fact]
    public async Task StartupMigrations_healthy_state_returns_200_regardless_of_token()
    {
        using var anonymousClient = _factory.CreateClient();
        var anonymousResponse = await anonymousClient.GetAsync("/health/startup-migrations");
        Assert.Equal(HttpStatusCode.OK, anonymousResponse.StatusCode);

        using var authorizedClient = _factory.CreateClient();
        authorizedClient.DefaultRequestHeaders.Add("X-Health-Token", DetailToken);
        var authorizedResponse = await authorizedClient.GetAsync("/health/startup-migrations");
        Assert.Equal(HttpStatusCode.OK, authorizedResponse.StatusCode);
    }

    public sealed class Factory : ApiWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            // "Test" (not "Staging"/"Production"): HasDetailAccess only auto-bypasses the token
            // check for Development, so "Test" still exercises the token gate exactly like
            // Staging/Production would — but unlike Staging it is one of the environment names the
            // Ticket 2/3 fail-closed checks (malware scanning, local storage fallbacks) already
            // treat as an explicit automated-test environment, so the host can actually start
            // without needing to fight those checks' own config requirements (they read
            // configuration eagerly during service registration, before any
            // ConfigureAppConfiguration override applied here takes effect).
            builder.UseEnvironment("Test");

            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["HealthChecks:ReadinessDetailToken"] = DetailToken,
                });
            });
        }
    }
}
