using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using HR.Modules.Marketing.Persistence;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Marketing.Features.ReorderMarketingRoadmapItems;

internal sealed class ReorderMarketingRoadmapItemsHandler(
    MarketingDbContext dbContext,
    ICurrentUser currentUser,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<ReorderMarketingRoadmapItemsResponse>> HandleAsync(
        ReorderMarketingRoadmapItemsRequest request,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, Guid.Empty, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await dbContext.TryReplayAsync<IdempotencyRecord, ReorderMarketingRoadmapItemsResponse>(
                scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<ReorderMarketingRoadmapItemsResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var now = clock.UtcNowOffset();
        var orderedIds = request.OrderedIds;

        var items = await dbContext.MarketingRoadmapItems
            .Where(r => orderedIds.Contains(r.Id))
            .ToListAsync(cancellationToken);

        if (items.Count != orderedIds.Count)
        {
            return Result.Failure<ReorderMarketingRoadmapItemsResponse>(
                Error.NotFound("One or more marketing roadmap items could not be found."));
        }

        var previousOrder = items
            .OrderBy(r => r.DisplayOrder)
            .Select(r => r.Id)
            .ToList();

        var byId = items.ToDictionary(r => r.Id);
        for (var index = 0; index < orderedIds.Count; index++)
        {
            byId[orderedIds[index]].SetDisplayOrder(index, currentUser.UserId, now);
        }

        var response = new ReorderMarketingRoadmapItemsResponse(
            orderedIds.Select((id, index) => new ReorderedMarketingRoadmapItemDto(id, index)).ToList());

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
            new MarketingRoadmapItemsReorderedAuditEvent(
                currentUser.UserId,
                now,
                previousOrder,
                orderedIds.ToList()),
            cancellationToken);

        return Result.Success(response);
    }
}
