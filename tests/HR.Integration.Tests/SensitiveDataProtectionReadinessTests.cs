using System.Text.Json;

using HR.Integration.Tests.Infrastructure;

namespace HR.Integration.Tests;

/// <summary>
/// Ticket 9: the "sensitive-data-encryption" readiness check (registered by
/// InfrastructureModule.AddSensitiveDataProtection) reports Healthy through the public readiness
/// endpoint when the host has valid encryption keys configured, as the shared
/// <see cref="ApiWebApplicationFactory"/> always does for integration tests.
/// </summary>
public sealed class SensitiveDataProtectionReadinessTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public SensitiveDataProtectionReadinessTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Readiness_detail_reports_sensitive_data_encryption_check_as_Healthy()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        // Not asserting the overall HTTP status / "status" field here: this shared test host's
        // "auth" check (live Supabase Auth reachability) is a separate, unrelated critical
        // dependency that can independently be Unhealthy in this environment (e.g. no live
        // Supabase project reachable from the test runner), which would make an overall-status
        // assertion flaky for a reason unrelated to what this test verifies. Ticket 9 only
        // requires that the "sensitive-data-encryption" check itself reports Healthy when
        // encryption is configured — asserted below regardless of the other checks' outcomes.
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);

        // The base ApiWebApplicationFactory host runs as Development, so the readiness endpoint
        // includes full per-check detail without needing the X-Health-Token header.
        Assert.True(doc.RootElement.TryGetProperty("checks", out var checks));

        var encryptionCheck = checks.EnumerateArray()
            .Single(entry => entry.GetProperty("name").GetString() == "sensitive-data-encryption");

        Assert.Equal("Healthy", encryptionCheck.GetProperty("status").GetString());
        Assert.False(encryptionCheck.GetProperty("critical").GetBoolean());
    }
}
