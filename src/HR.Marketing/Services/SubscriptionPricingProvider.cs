using System.Net.Http.Json;

using HR.SharedKernel.Pricing;

using Microsoft.Extensions.Caching.Memory;

namespace HR.Marketing.Services;

public sealed class SubscriptionPricingProvider(
    IHttpClientFactory httpClientFactory,
    IMemoryCache cache,
    ILogger<SubscriptionPricingProvider> logger)
{
    internal const string CacheKey = "marketing.subscription-pricing-config";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public async Task<SubscriptionPricingConfig> GetAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(CacheKey, out SubscriptionPricingConfig? cached) && cached is not null)
        {
            return cached;
        }

        var config = await FetchAsync(cancellationToken) ?? SubscriptionPricingConfig.Default;
        cache.Set(CacheKey, config, CacheTtl);
        return config;
    }

    private async Task<SubscriptionPricingConfig?> FetchAsync(CancellationToken cancellationToken)
    {
        try
        {
            var http = httpClientFactory.CreateClient("hrapi");
            var dto = await http.GetFromJsonAsync<PublicPricingDto>(
                "api/public/subscription-pricing", cancellationToken);

            if (dto?.Bands is not { Count: > 0 })
            {
                return null;
            }

            var config = new SubscriptionPricingConfig(
                dto.Bands
                    .Select(b => new SubscriptionPricingBand(b.StartEmployee, b.EndEmployee, b.PricePerEmployee))
                    .ToList(),
                dto.MinimumMonthlyChargeGbp);

            if (config.Validate().IsFailure)
            {
                logger.LogWarning("Public subscription pricing feed returned a structurally invalid config; using default.");
                return null;
            }

            return config;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not fetch public subscription pricing; using default.");
            return null;
        }
    }

    private sealed record PublicPricingDto(List<PublicPricingBandDto>? Bands, decimal MinimumMonthlyChargeGbp);

    private sealed record PublicPricingBandDto(int StartEmployee, int? EndEmployee, decimal PricePerEmployee);
}
