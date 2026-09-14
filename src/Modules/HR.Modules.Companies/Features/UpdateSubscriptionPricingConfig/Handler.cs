using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using HR.SharedKernel.Pricing;

using Microsoft.AspNetCore.Http;
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
        var scope = new IdempotencyScope(GetType().Name, Guid.Empty, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await dbContext.TryReplayAsync<IdempotencyRecord, UpdateSubscriptionPricingConfigResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<UpdateSubscriptionPricingConfigResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

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

        var saved = settings.GetPricingConfig();

        var response = new UpdateSubscriptionPricingConfigResponse(
            saved.Bands
                .Select(b => new UpdateSubscriptionPricingBandDto(b.StartEmployee, b.EndEmployee, b.PricePerEmployee))
                .ToList(),
            saved.MinimumMonthlyChargeGbp,
            settings.UpdatedAt,
            settings.UpdatedByUserId);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await dbContext.SaveIdempotentAsync(
                dbContext.IdempotencyRecords, scope, key, fingerprint!, StatusCodes.Status200OK, response, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

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

        return Result.Success(response);
    }
}
