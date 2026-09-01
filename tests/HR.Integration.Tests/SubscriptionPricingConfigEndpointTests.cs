using System.Net;
using System.Net.Http.Json;

using HR.Integration.Tests.Infrastructure;
using HR.Modules.Companies.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Story 4 — configurable subscription pricing. GET/PUT are behind the "platform:admin" policy
/// (same DB-backed check as GetPlatformSettingsEndpointTests); the anonymous public feed is
/// covered by <see cref="GetPublicSubscriptionPricingEndpointTests"/>.
/// </summary>
[Collection("Integration")]
public class SubscriptionPricingConfigEndpointTests
{
    private const string Route = "/api/companies/admin/subscription-pricing-config";

    private readonly ApiWebApplicationFactory _factory;

    public SubscriptionPricingConfigEndpointTests(ApiWebApplicationFactory factory)
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

    private async Task ResetSingletonRowAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        await db.PlatformSettings.ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Get_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Returns_Default_Bands_On_First_Call()
    {
        await ResetSingletonRowAsync();
        using var client = await AuthenticatedClientAsync();

        var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<PricingConfigPayload>();
        Assert.NotNull(payload);
        Assert.Equal(3, payload!.Bands.Count);
        Assert.Equal(20.00m, payload.MinimumMonthlyChargeGbp);
        Assert.Null(payload.Bands[^1].EndEmployee);
    }

    [Fact]
    public async Task Put_Persists_Valid_Config_And_Get_Reflects_It()
    {
        await ResetSingletonRowAsync();
        using var client = await AuthenticatedClientAsync();

        var request = new
        {
            bands = new object[]
            {
                new { startEmployee = 1, endEmployee = (int?)25, pricePerEmployee = 4.00m },
                new { startEmployee = 26, endEmployee = (int?)null, pricePerEmployee = 3.00m },
            },
            minimumMonthlyChargeGbp = 30.00m,
        };

        var putResponse = await client.PutAsJsonAsync(Route, request);
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        var getResponse = await client.GetAsync(Route);
        var payload = await getResponse.Content.ReadFromJsonAsync<PricingConfigPayload>();
        Assert.NotNull(payload);
        Assert.Equal(2, payload!.Bands.Count);
        Assert.Equal(30.00m, payload.MinimumMonthlyChargeGbp);
    }

    [Fact]
    public async Task Put_Rejects_Structurally_Invalid_Config()
    {
        await ResetSingletonRowAsync();
        using var client = await AuthenticatedClientAsync();

        // Gap between band 1 (ends 50) and band 2 (starts 60).
        var request = new
        {
            bands = new object[]
            {
                new { startEmployee = 1, endEmployee = (int?)50, pricePerEmployee = 4.00m },
                new { startEmployee = 60, endEmployee = (int?)null, pricePerEmployee = 3.00m },
            },
            minimumMonthlyChargeGbp = 30.00m,
        };

        var putResponse = await client.PutAsJsonAsync(Route, request);

        Assert.Equal(HttpStatusCode.BadRequest, putResponse.StatusCode);
    }

    [Fact]
    public async Task Put_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync(Route, new
        {
            bands = new object[] { new { startEmployee = 1, endEmployee = (int?)null, pricePerEmployee = 2.00m } },
            minimumMonthlyChargeGbp = 20.00m,
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed record PricingConfigPayload(
        List<PricingBandPayload> Bands,
        decimal MinimumMonthlyChargeGbp);

    private sealed record PricingBandPayload(int StartEmployee, int? EndEmployee, decimal PricePerEmployee);
}
