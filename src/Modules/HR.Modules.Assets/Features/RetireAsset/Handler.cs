using HR.Modules.Assets.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Features.RetireAsset;

internal sealed class RetireAssetHandler(AssetsDbContext db, IClock clock)
{
    public async Task<Result> HandleAsync(
        RetireAssetRequest request,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, object?>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success();
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var asset = await db.Assets
            .FirstOrDefaultAsync(a => a.Id == request.Id && a.CompanyId == request.CompanyId, cancellationToken);

        if (asset is null)
            return Result.Failure(Error.NotFound("Asset not found."));

        var now = new DateTimeOffset(clock.UtcNow, TimeSpan.Zero);

        try
        {
            asset.Retire(now);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(Error.Conflict(ex.Message));
        }

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await db.SaveIdempotentAsync<IdempotencyRecord, object?>(db.IdempotencyRecords, 
                scope, key, fingerprint!, StatusCodes.Status204NoContent, null, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success();
        }
        else
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }
}
