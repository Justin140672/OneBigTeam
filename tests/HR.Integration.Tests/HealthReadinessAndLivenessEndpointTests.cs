using System.Net;
using System.Text.Json;

using HR.Integration.Tests.Infrastructure;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HR.Integration.Tests;

public sealed class HealthReadinessAndLivenessEndpointTests
    : IClassFixture<HealthReadinessAndLivenessEndpointTests.Factory>
{
    private const string DetailToken = "nfr03-test-detail-token";

    private const string Secret = "Password=sup3r-s3cret;Host=db.internal.acme;Port=5432";

    private readonly Factory _factory;

    public HealthReadinessAndLivenessEndpointTests(Factory factory)
    {
        _factory = factory;
        Factory.CriticalDependencyDown = false;
    }

    [Fact]
    public async Task Alive_returns_200_even_when_a_critical_dependency_is_down()
    {
        Factory.CriticalDependencyDown = true;
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/alive");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Healthy", body);
        Assert.DoesNotContain("checks", body);
        Assert.DoesNotContain(Secret, body);
    }

    [Fact]
    public async Task Ready_returns_200_Degraded_when_only_optional_dependencies_are_down()
    {
        Factory.CriticalDependencyDown = false;
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await ReadStatusAsync(response);
        Assert.Equal("Degraded", payload);
    }

    [Fact]
    public async Task Ready_returns_503_when_a_critical_dependency_is_down()
    {
        Factory.CriticalDependencyDown = true;
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await ReadStatusAsync(response));
    }

    [Fact]
    public async Task Ready_public_body_discloses_no_check_detail_or_secrets()
    {
        Factory.CriticalDependencyDown = true;
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("checks", body);
        Assert.DoesNotContain("description", body);
        AssertNoInfrastructureDisclosure(body);
    }

    [Fact]
    public async Task Ready_with_invalid_token_stays_minimal()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Health-Token", "wrong-token");

        var response = await client.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("checks", body);
        AssertNoInfrastructureDisclosure(body);
    }

    [Fact]
    public async Task Ready_with_valid_token_returns_detail_but_never_exceptions_or_connection_data()
    {
        Factory.CriticalDependencyDown = true;
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Health-Token", DetailToken);

        var response = await client.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("checks", out var checks));
        Assert.True(checks.GetArrayLength() > 0);
        AssertNoInfrastructureDisclosure(body);
        Assert.DoesNotContain("stackTrace", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exception", body, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> ReadStatusAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("status").GetString()!;
    }

    private static void AssertNoInfrastructureDisclosure(string body)
    {
        Assert.DoesNotContain(Secret, body);
        Assert.DoesNotContain("Password=", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("db.internal.acme", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Port=5432", body, StringComparison.OrdinalIgnoreCase);
    }

    public sealed class Factory : ApiWebApplicationFactory
    {
        public static bool CriticalDependencyDown { get; set; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.UseEnvironment("Test");

            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["HealthChecks:ReadinessDetailToken"] = DetailToken,
                });
            });

            builder.ConfigureServices(services =>
            {
                services.AddHealthChecks()
                    .AddCheck("nfr03_toggle_critical", () => CriticalDependencyDown
                        ? HealthCheckResult.Unhealthy("Simulated critical dependency outage.")
                        : HealthCheckResult.Healthy(), tags: ["ready", "critical"])
                    .AddCheck("nfr03_secret_probe", () => HealthCheckResult.Unhealthy(
                        description: "Optional dependency unavailable.",
                        exception: new InvalidOperationException(Secret),
                        data: new Dictionary<string, object> { ["connectionString"] = Secret }),
                        tags: ["degraded"]);
            });
        }
    }
}
