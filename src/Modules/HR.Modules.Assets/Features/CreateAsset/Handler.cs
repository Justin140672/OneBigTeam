using HR.Modules.Assets.Domain;
using HR.Modules.Assets.Persistence;
using HR.Modules.Companies.Contracts;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using HR.SharedKernel.Outbox;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Features.CreateAsset;

internal sealed class CreateAssetHandler(
    AssetsDbContext db,
    IClock clock,
    ICompanyAssetNumberSettingsReader assetNumberSettingsReader,
    IAssetNumberGenerator assetNumberGenerator)
{
    public async Task<Result<CreateAssetResponse>> HandleAsync(
        CreateAssetRequest request,
        CancellationToken cancellationToken)
    {
        // Ticket 3 (P1) follow-up item 3: the security/ownership boundary this key is scoped to -
        // the client-supplied key alone is never trusted as identity.
        var scope = new IdempotencyScope(nameof(CreateAssetHandler), request.CompanyId, request.ActorId);

        // Ticket 3 (P1) follow-up: dedupe a retried/duplicated request up front, before generating
        // a new auto-numbered asset, so a repeated delivery can't create a second asset for the
        // same logical request. SaveIdempotentAsync below also catches a same-key request that
        // races in concurrently.
        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, CreateAssetResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<CreateAssetResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var categoryExists = await db.AssetCategories.AnyAsync(
            c => c.Id == request.CategoryId && c.CompanyId == request.CompanyId && c.IsActive,
            cancellationToken);

        if (!categoryExists)
            return Result.Failure<CreateAssetResponse>(
                Error.NotFound("Asset category not found."));

        var assetNumberMode = await assetNumberSettingsReader.GetModeAsync(request.CompanyId, cancellationToken);

        string assetNumber;

        if (string.IsNullOrWhiteSpace(request.AssetNumber))
        {
            if (assetNumberMode == AssetNumberMode.Manual)
            {
                return Result.Failure<CreateAssetResponse>(
                    Error.Validation("Asset number is required."));
            }

            // Automatic mode: caller didn't supply one, generate it via the atomic counter and
            // retry on conflict — mirrors CreateEmployeeHandler's own EmployeeNumber generation.
            const int maxAttempts = 5;
            var attempt = 0;
            while (true)
            {
                attempt++;
                assetNumber = await assetNumberGenerator.GenerateNextAsync(request.CompanyId, cancellationToken);

                var candidateExists = await db.Assets.AnyAsync(
                    a => a.CompanyId == request.CompanyId && a.AssetNumber == assetNumber,
                    cancellationToken);

                if (!candidateExists)
                    break;

                if (attempt >= maxAttempts)
                {
                    return Result.Failure<CreateAssetResponse>(
                        Error.Conflict("Could not generate a unique asset number after several attempts."));
                }
            }
        }
        else
        {
            if (assetNumberMode == AssetNumberMode.Automatic)
            {
                return Result.Failure<CreateAssetResponse>(
                    Error.Validation("Asset number is auto-generated for this company and must be left blank."));
            }

            assetNumber = request.AssetNumber.Trim();

            var exists = await db.Assets.AnyAsync(
                a => a.CompanyId == request.CompanyId && a.AssetNumber == assetNumber,
                cancellationToken);

            if (exists)
                return Result.Failure<CreateAssetResponse>(
                    Error.Conflict($"An asset with number '{assetNumber}' already exists."));
        }

        var now = new DateTimeOffset(clock.UtcNow, TimeSpan.Zero);
        var entity = Asset.Create(
            Guid.NewGuid(), request.CompanyId, assetNumber, request.CategoryId,
            request.Name, request.Manufacturer, request.Model,
            request.SerialNumber, request.PurchaseDate, request.PurchasePrice, now);

        db.Assets.Add(entity);

        // Built from in-memory values ahead of the save, so it can double as both the response and
        // the payload persisted for an idempotency replay.
        var response = new CreateAssetResponse(
            entity.Id, entity.CompanyId, entity.AssetNumber, entity.CategoryId,
            entity.Name, entity.Manufacturer, entity.Model,
            entity.SerialNumber, entity.PurchaseDate, entity.PurchasePrice,
            entity.Status.ToString(), entity.CreatedAt, entity.UpdatedAt);

        // Ticket 3 (P1) follow-up item 5: stage the audit intent in the SAME transaction as the
        // business write and the idempotency record, instead of publishing after commit. On an
        // idempotent replay (below) this line is never reached, so a replay can never enqueue a
        // second outbox row for the same logical asset creation.
        db.AuditOutboxEntries.EnqueueAuditOutbox(new AssetCreatedAuditEvent(
            entity.CompanyId,
            entity.Id,
            entity.AssetNumber,
            entity.Name,
            now), request.CompanyId, now);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await db.SaveIdempotentAsync(db.IdempotencyRecords,
                scope, key, fingerprint!, StatusCodes.Status201Created, response, now, cancellationToken);

            // Lost a race against a concurrent duplicate under the same key - this attempt's asset
            // row and staged outbox entry were rolled back along with it, so skip returning our own
            // result and hand back the winner's result untouched. Its own outbox row continues
            // delivery independently.
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
