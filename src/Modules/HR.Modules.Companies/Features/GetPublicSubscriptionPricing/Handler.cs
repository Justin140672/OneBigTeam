using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Pricing;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Features.GetPublicSubscriptionPricing;

internal sealed class GetPublicSubscriptionPricingHandler(PlatformDbContext dbContext)
{
    public async Task<Result<GetPublicSubscriptionPricingResponse>> HandleAsync(
        GetPublicSubscriptionPricingRequest request,
        CancellationToken cancellationToken)
    {
        var settings = await dbContext.PlatformSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == PlatformSettings.SingletonId, cancellationToken);

        var config = settings?.GetPricingConfig() ?? SubscriptionPricingConfig.Default;

        return Result.Success(new GetPublicSubscriptionPricingResponse(
            config.Bands
                .Select(b => new PublicSubscriptionPricingBandDto(b.StartEmployee, b.EndEmployee, b.PricePerEmployee))
                .ToList(),
            config.MinimumMonthlyChargeGbp));
    }
}
