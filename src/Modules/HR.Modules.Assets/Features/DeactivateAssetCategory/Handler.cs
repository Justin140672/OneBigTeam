using HR.Modules.Assets.Domain;
using HR.Modules.Assets.Persistence;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Features.DeactivateAssetCategory;

internal sealed class DeactivateAssetCategoryHandler(
    AssetsDbContext db,
    IClock clock,
    IPositionProfileAssetsReader positionProfileAssetsReader)
{
    public async Task<Result> HandleAsync(
        DeactivateAssetCategoryRequest request,
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

        var category = await db.AssetCategories
            .FirstOrDefaultAsync(c => c.Id == request.Id && c.CompanyId == request.CompanyId, cancellationToken);

        if (category is null)
            return Result.Failure(Error.NotFound("Asset category not found."));

        var activeAssetCount = await db.Assets
            .CountAsync(
                a => a.CategoryId == request.Id
                  && a.CompanyId == request.CompanyId
                  && a.Status != AssetStatus.Retired,
                cancellationToken);

        var activePositionProfileReferenceCount = await positionProfileAssetsReader
            .CountActiveReferencesToAssetCategoryAsync(request.CompanyId, request.Id, cancellationToken);

        var usageSegments = new List<string>();
        if (activeAssetCount > 0)
            usageSegments.Add($"{activeAssetCount} active asset{(activeAssetCount == 1 ? "" : "s")}");
        if (activePositionProfileReferenceCount > 0)
        {
            usageSegments.Add(
                $"{activePositionProfileReferenceCount} active position profile" +
                $"{(activePositionProfileReferenceCount == 1 ? "" : "s")}");
        }

        if (usageSegments.Count > 0)
        {
            return Result.Failure(Error.Conflict(
                $"Cannot deactivate '{category.Name}' — it is used on " +
                $"{string.Join(" and ", usageSegments)}."));
        }

        var now = new DateTimeOffset(clock.UtcNow, TimeSpan.Zero);
        category.Deactivate(now);

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
