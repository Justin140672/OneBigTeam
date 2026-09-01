using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Pricing;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Features.UpdateSubscriptionPricingConfig;

internal sealed class UpdateSubscriptionPricingConfigHandler(
    CompaniesDbContext dbContext,
    ICurrentUser currentUser,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<UpdateSubscriptionPricingConfigResponse>> HandleAsync(
        UpdateSubscriptionPricingConfigRequest request,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset();

        var settings = await dbContext.PlatformSettings
            .SingleOrDefaultAsync(s => s.Id == PlatformSettings.SingletonId, cancellationToken);

        if (settings is null)
        {
            settings = PlatformSettings.CreateDefault(now);
            dbContext.PlatformSettings.Add(settings);
        }

        var previousState = new SubscriptionPricingConfigAuditSnapshot(
            settings.PricingBandsJson,
            settings.MinimumMonthlyChargeGbp);

        var config = new SubscriptionPricingConfig(
            (request.Bands ?? [])
                .Select(b => new SubscriptionPricingBand(b.StartEmployee, b.EndEmployee, b.PricePerEmployee))
                .ToList(),
            request.MinimumMonthlyChargeGbp);

        var updateResult = settings.UpdatePricingConfig(config, currentUser.UserId, now);
        if (updateResult.IsFailure)
        {
            return Result.Failure<UpdateSubscriptionPricingConfigResponse>(updateResult.Error);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new SubscriptionPricingConfigUpdatedAuditEvent(
                settings.Id,
                currentUser.UserId,
                now,
                previousState,
                new SubscriptionPricingConfigAuditSnapshot(
                    settings.PricingBandsJson,
                    settings.MinimumMonthlyChargeGbp)),
            cancellationToken);

        var saved = settings.GetPricingConfig();

        return Result.Success(new UpdateSubscriptionPricingConfigResponse(
            saved.Bands
                .Select(b => new UpdateSubscriptionPricingBandDto(b.StartEmployee, b.EndEmployee, b.PricePerEmployee))
                .ToList(),
            saved.MinimumMonthlyChargeGbp,
            settings.UpdatedAt,
            settings.UpdatedByUserId));
    }
}
