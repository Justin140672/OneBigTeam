using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using HR.Infrastructure.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.AddRequiredAssetToPositionProfile;

internal sealed class AddRequiredAssetHandler(
    EmployeesDbContext dbContext,
    IAssetCategoryReader assetCategoryReader,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<AddRequiredAssetResponse>> HandleAsync(
        AddRequiredAssetRequest request,
        Guid actorEmployeeId,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await dbContext.TryReplayAsync<IdempotencyRecord, AddRequiredAssetResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<AddRequiredAssetResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var profileExists = await dbContext.PositionProfiles
            .AnyAsync(
                p => p.Id == request.PositionProfileId && p.CompanyId == request.CompanyId,
                cancellationToken);

        if (!profileExists)
            return Result.Failure<AddRequiredAssetResponse>(
                Error.NotFound($"Position profile '{request.PositionProfileId}' was not found."));

        var assetCategoryExists = await assetCategoryReader.ExistsAsync(
            request.CompanyId, request.AssetCategoryId, cancellationToken);

        if (!assetCategoryExists)
            return Result.Failure<AddRequiredAssetResponse>(
                Error.NotFound($"Asset category '{request.AssetCategoryId}' was not found."));

        var duplicateExists = await dbContext.PositionProfileRequiredAssets
            .AnyAsync(
                a => a.PositionProfileId == request.PositionProfileId &&
                     a.AssetCategoryId == request.AssetCategoryId &&
                     a.IsActive,
                cancellationToken);

        if (duplicateExists)
            return Result.Failure<AddRequiredAssetResponse>(
                Error.Conflict("This asset category is already required for the position profile."));

        var now = clock.UtcNowOffset();

        var requiredAsset = PositionProfileRequiredAsset.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.PositionProfileId,
            request.AssetCategoryId,
            request.IsMandatory,
            request.Quantity,
            actorEmployeeId,
            now);

        dbContext.PositionProfileRequiredAssets.Add(requiredAsset);

        var response = new AddRequiredAssetResponse(
            requiredAsset.Id,
            requiredAsset.PositionProfileId,
            requiredAsset.AssetCategoryId,
            requiredAsset.IsMandatory,
            requiredAsset.Quantity);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await dbContext.SaveIdempotentAsync<IdempotencyRecord, AddRequiredAssetResponse>(dbContext.IdempotencyRecords, 
                scope, key, fingerprint!, StatusCodes.Status201Created, response, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await auditEventPublisher.PublishAsync(
            new RequiredAssetAddedAuditEvent(
                request.CompanyId,
                request.PositionProfileId,
                requiredAsset.Id,
                request.AssetCategoryId,
                actorEmployeeId,
                now),
            cancellationToken);

        return Result.Success(response);
    }
}
