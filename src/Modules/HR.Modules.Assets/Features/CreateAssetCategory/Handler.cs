using HR.Modules.Assets.Domain;
using HR.Modules.Assets.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Features.CreateAssetCategory;

internal sealed class CreateAssetCategoryHandler(AssetsDbContext db, IClock clock)
{
    public async Task<Result<CreateAssetCategoryResponse>> HandleAsync(
        CreateAssetCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, CreateAssetCategoryResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<CreateAssetCategoryResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        // Was previously missing entirely — an asset category name had no uniqueness check at all
        // (case-sensitive or otherwise). Added here case-insensitively, matching every other
        // "Name must be unique per company" entity in this codebase.
        var nameExists = await db.AssetCategories.AnyAsync(
            c => c.CompanyId == request.CompanyId && c.Name.ToLower() == request.Name.Trim().ToLower(),
            cancellationToken);

        if (nameExists)
        {
            return Result.Failure<CreateAssetCategoryResponse>(
                Error.Conflict($"An asset category named '{request.Name.Trim()}' already exists."));
        }

        var now = new DateTimeOffset(clock.UtcNow, TimeSpan.Zero);
        var entity = AssetCategory.Create(Guid.NewGuid(), request.CompanyId, request.Name, request.Description, now);

        db.AssetCategories.Add(entity);

        var response = new CreateAssetCategoryResponse(
            entity.Id, entity.CompanyId, entity.Name, entity.Description,
            entity.IsActive, entity.CreatedAt, entity.UpdatedAt);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await db.SaveIdempotentAsync(db.IdempotencyRecords, 
                scope, key, fingerprint!, StatusCodes.Status201Created, response, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(response);
    }
}
