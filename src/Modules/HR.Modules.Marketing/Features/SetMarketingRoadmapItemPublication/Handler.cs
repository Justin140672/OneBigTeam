using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using HR.Modules.Marketing.Persistence;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Marketing.Features.SetMarketingRoadmapItemPublication;

internal sealed class SetMarketingRoadmapItemPublicationHandler(
    MarketingDbContext dbContext,
    ICurrentUser currentUser,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<SetMarketingRoadmapItemPublicationResponse>> HandleAsync(
        SetMarketingRoadmapItemPublicationRequest request,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, Guid.Empty, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await dbContext.TryReplayAsync<IdempotencyRecord, SetMarketingRoadmapItemPublicationResponse>(
                scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<SetMarketingRoadmapItemPublicationResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var now = clock.UtcNowOffset();

        var item = await dbContext.MarketingRoadmapItems
            .SingleOrDefaultAsync(r => r.Id == request.Id, cancellationToken);

        if (item is null)
        {
            return Result.Failure<SetMarketingRoadmapItemPublicationResponse>(
                Error.NotFound("Marketing roadmap item not found."));
        }

        var previouslyPublished = item.IsPublished;

        if (request.IsPublished)
        {
            item.Publish(currentUser.UserId, now);
        }
        else
        {
            item.Unpublish(currentUser.UserId, now);
        }

        var response = new SetMarketingRoadmapItemPublicationResponse(
            item.Id,
            item.IsPublished,
            item.UpdatedAt,
            item.UpdatedByUserId);

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
            new MarketingRoadmapItemPublicationChangedAuditEvent(
                item.Id,
                currentUser.UserId,
                now,
                previouslyPublished,
                item.IsPublished),
            cancellationToken);

        return Result.Success(response);
    }
}
