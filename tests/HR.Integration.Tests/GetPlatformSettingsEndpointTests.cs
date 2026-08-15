using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Companies.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Unlike the other "platform:admin" Companies endpoints (e.g. ExtendCustomerTrial, ListCustomers),
/// GetPlatformSettings/UpdatePlatformSettings do not additionally gate on a
/// PlatformAdmin:AllowedEmails allow-list or an identity.platform_administrators row inside the
/// handler — the "platform:admin" FastEndpoints policy (RequireAuthenticatedUser, see
/// IdentityModule.AddRolePolicies) is the only authorization check for these two endpoints. So any
/// authenticated caller succeeds; only an anonymous request is rejected (401).
/// </summary>
[Collection("Integration")]
public class GetPlatformSettingsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public GetPlatformSettingsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient AuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, Guid.NewGuid().ToString());
        return client;
    }

    [Fact]
    public async Task Get_PlatformSettings_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/companies/admin/platform-settings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// PlatformSettings is a true singleton row (fixed id), so — unlike the per-entity
    /// Guid.NewGuid() rows most other integration tests create — it is shared/mutated across every
    /// test method in this xUnit collection. Deleting it first makes this test's "first call sees
    /// the lazy-seeded default" assertion independent of execution order relative to
    /// UpdatePlatformSettingsEndpointTests (which mutates the same row).
    /// </summary>
    private async Task ResetSingletonRowAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        await db.PlatformSettings.ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Get_PlatformSettings_Returns_Default_Seeded_Values_On_First_Call()
    {
        await ResetSingletonRowAsync();

        using var client = AuthenticatedClient();

        var response = await client.GetAsync("/api/companies/admin/platform-settings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<PlatformSettingsPayload>();
        Assert.NotNull(payload);
        Assert.Equal(14, payload!.TrialLengthDays);
        Assert.Equal(20.00m, payload.DefaultMonthlyPriceGbp);
        Assert.Equal("support@hrplatform.com", payload.SupportEmail);
        Assert.False(payload.MaintenanceModeEnabled);
        Assert.Null(payload.MaintenanceModeMessage);
        Assert.Empty(payload.FeatureFlags);
    }

    internal sealed record PlatformSettingsPayload(
        int TrialLengthDays,
        decimal DefaultMonthlyPriceGbp,
        string SupportEmail,
        bool MaintenanceModeEnabled,
        string? MaintenanceModeMessage,
        Dictionary<string, bool> FeatureFlags,
        DateTimeOffset UpdatedAt,
        Guid? UpdatedByUserId);
}
