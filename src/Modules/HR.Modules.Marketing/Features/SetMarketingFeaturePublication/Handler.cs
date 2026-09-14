using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using HR.Modules.Marketing.Persistence;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Marketing.Features.SetMarketingFeaturePublication;

internal sealed class SetMarketingFeaturePublicationHandler(
    MarketingDbContext dbContext,
    ICurrentUser currentUser,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<SetMarketingFeaturePublicationResponse>> HandleAsync(
        SetMarketingFeaturePublicationRequest request,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, Guid.Empty, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await dbContext.TryReplayAsync<IdempotencyRecord, SetMarketingFeaturePublicationResponse>(
                scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<SetMarketingFeaturePublicationResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var now = clock.UtcNowOffset();

        var feature = await dbContext.MarketingFeatures
            .SingleOrDefaultAsync(f => f.Id == request.Id, cancellationToken);

        if (feature is null)
        {
            return Result.Failure<SetMarketingFeaturePublicationResponse>(
                Error.NotFound("Marketing feature not found."));
        }

        var previouslyPublished = feature.IsPublished;

        if (request.IsPublished)
        {
            feature.Publish(currentUser.UserId, now);
        }
        else
        {
            feature.Unpublish(currentUser.UserId, now);
        }

        var response = new SetMarketingFeaturePublicationResponse(
            feature.Id,
            feature.IsPublished,
            feature.UpdatedAt,
            feature.UpdatedByUserId);

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
            new MarketingFeaturePublicationChangedAuditEvent(
                feature.Id,
                currentUser.UserId,
                now,
                previouslyPublished,
                feature.IsPublished),
            cancellationToken);

        return Result.Success(response);
    }
}
