using System.Net;
using System.Text.Json;

using HR.Integration.Tests.Infrastructure;

namespace HR.Integration.Tests;

/// <summary>
/// Ticket 5: the anonymous <c>GET /health/release</c> running-release probe and the back-compatible
/// <c>release</c> block added to <c>GET /health/startup-migrations</c>. The deploy pipeline verifies
/// a release by EXACT sha match against these payloads, so this pins their shape and that they stay
/// anonymous and disclose nothing beyond service/sha/version/environment.
/// </summary>
[Collection("Integration")]
public sealed class HealthReleaseEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public HealthReleaseEndpointTests(ApiWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Release_endpoint_is_anonymous_and_exposes_only_the_safe_fields()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/release");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var names = doc.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(
            new[] { "deploymentId", "environment", "railwayCommit", "railwayEnvironmentId", "railwayServiceId", "service", "sha", "startedAt", "version" },
            names);
        Assert.Equal("api", doc.RootElement.GetProperty("service").GetString());
        // Railway-injected immutable identity fields are present (null in the test host, which has no
        // Railway env vars) and are separate from the mutable display 'sha'/'version'.
        Assert.True(doc.RootElement.TryGetProperty("deploymentId", out _));

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ConnectionString", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Startup_migrations_payload_keeps_module_keys_and_adds_a_release_sibling()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/startup-migrations");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // Back-compat: module names are still top-level keys with the {status,...} shape.
        Assert.True(doc.RootElement.TryGetProperty("companies", out var companies));
        Assert.True(companies.TryGetProperty("status", out _));

        // New: a sibling "release" object with sha + version (used by check-startup-migrations.ps1
        // to reject a healthy OLD instance).
        Assert.True(doc.RootElement.TryGetProperty("release", out var release));
        Assert.True(release.TryGetProperty("sha", out _));
        Assert.True(release.TryGetProperty("version", out _));
    }
}
