using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Companies.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// The "platform:admin" FastEndpoints policy now enforces a real DB-backed check (SEC-002 fix —
/// see PlatformAdminAuthorizationHandler) on top of RequireAuthenticatedUser: the caller must match
/// an enabled identity.platform_administrators row. See PlatformSettingsAuthorizationTests for the
/// full authorization matrix (anonymous / no-role / employee / company admin / hr admin / disabled
/// admin / enabled admin). This file only seeds a platform administrator for its own
/// success-path/business-behaviour assertions.
/// </summary>
[Collection("Integration")]
public class GetPlatformSettingsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public GetPlatformSettingsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> AuthenticatedClientAsync()
    {
        var userId = Guid.NewGuid();
        await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory,
            HR.Modules.Identity.Domain.PlatformAdministratorRole.SupportStaff,
            isEnabled: true,
            supabaseAuthUserId: userId);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
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

        using var client = await AuthenticatedClientAsync();

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
