using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using HR.Modules.Marketing.Persistence;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Marketing.Features.ReorderMarketingFeatures;

internal sealed class ReorderMarketingFeaturesHandler(
    MarketingDbContext dbContext,
    ICurrentUser currentUser,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<ReorderMarketingFeaturesResponse>> HandleAsync(
        ReorderMarketingFeaturesRequest request,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, Guid.Empty, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await dbContext.TryReplayAsync<IdempotencyRecord, ReorderMarketingFeaturesResponse>(
                scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<ReorderMarketingFeaturesResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var now = clock.UtcNowOffset();
        var orderedIds = request.OrderedIds;

        var features = await dbContext.MarketingFeatures
            .Where(f => orderedIds.Contains(f.Id))
            .ToListAsync(cancellationToken);

        if (features.Count != orderedIds.Count)
        {
            return Result.Failure<ReorderMarketingFeaturesResponse>(
                Error.NotFound("One or more marketing features could not be found."));
        }

        var previousOrder = features
            .OrderBy(f => f.DisplayOrder)
            .Select(f => f.Id)
            .ToList();

        var byId = features.ToDictionary(f => f.Id);
        for (var index = 0; index < orderedIds.Count; index++)
        {
            byId[orderedIds[index]].SetDisplayOrder(index, currentUser.UserId, now);
        }

        var response = new ReorderMarketingFeaturesResponse(
            orderedIds.Select((id, index) => new ReorderedMarketingFeatureDto(id, index)).ToList());

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await dbContext.SaveIdempotentAsync(
                dbContext.IdempotencyRecords, scope, key, fingerprint!, StatusCodes.Status200OK, response, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
            {
                return Result.Success(outcome.Response!);
            }
        }
        else
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await auditEventPublisher.PublishAsync(
            new MarketingFeaturesReorderedAuditEvent(
                currentUser.UserId,
                now,
                previousOrder,
                orderedIds.ToList()),
            cancellationToken);

        return Result.Success(response);
    }
}
