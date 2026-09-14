using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using HR.Modules.Marketing.Domain;
using HR.Modules.Marketing.Persistence;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Marketing.Features.CreateMarketingRoadmapItem;

internal sealed class CreateMarketingRoadmapItemHandler(
    MarketingDbContext dbContext,
    ICurrentUser currentUser,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<CreateMarketingRoadmapItemResponse>> HandleAsync(
        CreateMarketingRoadmapItemRequest request,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, Guid.Empty, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await dbContext.TryReplayAsync<IdempotencyRecord, CreateMarketingRoadmapItemResponse>(
                scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<CreateMarketingRoadmapItemResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var now = clock.UtcNowOffset();

        var product = await dbContext.MarketingProducts
            .SingleOrDefaultAsync(p => p.Id == MarketingProduct.SingletonId, cancellationToken);

        if (product is null)
        {
            product = MarketingProduct.CreateDefault(now);
            dbContext.MarketingProducts.Add(product);
        }

        var created = MarketingRoadmapItem.Create(
            Guid.NewGuid(),
            product.Id,
            request.Title,
            request.Description,
            request.IconName,
            request.DeliveryStatus,
            request.DisplayOrder,
            currentUser.UserId,
            now);

        if (created.IsFailure)
        {
            return Result.Failure<CreateMarketingRoadmapItemResponse>(created.Error);
        }

        var item = created.Value!;
        dbContext.MarketingRoadmapItems.Add(item);

        var response = new CreateMarketingRoadmapItemResponse(
            item.Id,
            item.Title,
            item.IsPublished,
            item.DisplayOrder,
            item.DeliveryStatus.ToString(),
            item.CreatedAt,
            item.UpdatedAt);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await dbContext.SaveIdempotentAsync(
                dbContext.IdempotencyRecords, scope, key, fingerprint!, StatusCodes.Status201Created, response, now, cancellationToken);

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
            new MarketingRoadmapItemCreatedAuditEvent(
                item.Id,
                currentUser.UserId,
                now,
                new MarketingRoadmapItemAuditSnapshot(
                    item.Title, item.Description, item.IsPublished, item.DisplayOrder,
                    item.DeliveryStatus.ToString())),
            cancellationToken);

        return Result.Success(response);
    }
}
