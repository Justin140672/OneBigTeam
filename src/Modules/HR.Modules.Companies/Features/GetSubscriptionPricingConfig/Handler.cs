using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.SharedKernel;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Features.GetSubscriptionPricingConfig;

/// <summary>
/// Returns the single authoritative configurable subscription pricing model (Story 4). Lazy-seeds
/// the PlatformSettings singleton on first read, mirroring GetPlatformSettingsHandler.
/// </summary>
internal sealed class GetSubscriptionPricingConfigHandler(CompaniesDbContext dbContext, IClock clock)
{
    public async Task<Result<GetSubscriptionPricingConfigResponse>> HandleAsync(
        GetSubscriptionPricingConfigRequest request,
        CancellationToken cancellationToken)
    {
        var settings = await dbContext.PlatformSettings
            .SingleOrDefaultAsync(s => s.Id == PlatformSettings.SingletonId, cancellationToken);

        if (settings is null)
        {
            settings = PlatformSettings.CreateDefault(clock.UtcNowOffset());
            dbContext.PlatformSettings.Add(settings);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var config = settings.GetPricingConfig();

        return Result.Success(new GetSubscriptionPricingConfigResponse(
            config.Bands
                .Select(b => new SubscriptionPricingBandDto(b.StartEmployee, b.EndEmployee, b.PricePerEmployee))
                .ToList(),
            config.MinimumMonthlyChargeGbp));
    }
}
