using System.Net;
using System.Net.Http.Json;

using HR.Integration.Tests.Infrastructure;
using HR.Modules.Companies.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Story 4 — the anonymous marketing feed for the configurable subscription pricing model.
/// No authentication; returns the default bands when the singleton has never been seeded.
/// </summary>
[Collection("Integration")]
public class GetPublicSubscriptionPricingEndpointTests
{
    private const string Route = "/api/public/subscription-pricing";

    private readonly ApiWebApplicationFactory _factory;

    public GetPublicSubscriptionPricingEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_Anonymous_Returns_Ok_With_Default_Bands()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
            await db.PlatformSettings.ExecuteDeleteAsync();
        }

        using var client = _factory.CreateClient();

        var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<PublicPricingPayload>();
        Assert.NotNull(payload);
        Assert.Equal(3, payload!.Bands.Count);
        Assert.Equal(20.00m, payload.MinimumMonthlyChargeGbp);
        Assert.Equal(1, payload.Bands[0].StartEmployee);
        Assert.Null(payload.Bands[^1].EndEmployee);
    }

    private sealed record PublicPricingPayload(
        List<PublicPricingBandPayload> Bands,
        decimal MinimumMonthlyChargeGbp);

    private sealed record PublicPricingBandPayload(int StartEmployee, int? EndEmployee, decimal PricePerEmployee);
}
