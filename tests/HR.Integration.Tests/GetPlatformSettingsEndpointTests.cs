using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Companies.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

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

    private async Task ResetSingletonRowAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
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
