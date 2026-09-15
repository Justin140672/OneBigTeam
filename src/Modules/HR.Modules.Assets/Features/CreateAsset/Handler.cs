using HR.Modules.Assets.Domain;
using HR.Modules.Assets.Persistence;
using HR.Modules.Companies.Contracts;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using HR.SharedKernel.Outbox;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Assets.Features.CreateAsset;

internal sealed class CreateAssetHandler(
    AssetsDbContext db,
    IClock clock,
    ICompanyAssetNumberSettingsReader assetNumberSettingsReader,
    IAssetNumberGenerator assetNumberGenerator,
    // Optional (like postCommitFaultInjector below) so the many existing handler-level unit tests
    // that construct this handler directly don't all need updating. Production DI always supplies
    // real instances via the required IAuditEventPublisher/ILogger registrations in Program.cs;
    // when null (unit tests), the inline post-commit outbox dispatch below is simply skipped and
    // the event stays queued for the background IdempotencyMaintenanceJob.
    IAuditEventPublisher? auditPublisher = null,
    ILogger<CreateAssetHandler>? logger = null,
    IPostCommitFaultInjector? postCommitFaultInjector = null)
{
    // Optional so the many existing handler-level unit tests that construct this handler directly
    // (with no interest in Ticket 3's fault-injection seam) don't all need updating for an unrelated
    // parameter. Production DI always supplies a real instance (NoOpPostCommitFaultInjector by
    // default) via the required interface registration in Program.cs.
    private readonly IPostCommitFaultInjector _postCommitFaultInjector = postCommitFaultInjector ?? new NoOpPostCommitFaultInjector();

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

        // Ticket 3 (P1) final gap item 6: no-op in production. Lets an integration test simulate a
        // failure BEFORE this save commits (so nothing is persisted, including no asset number
        // consumed), proving a retry with the same Idempotency-Key performs and commits the mutation
        // exactly once.
        await _postCommitFaultInjector.MaybeFailAfterCommitAsync(
            $"{nameof(CreateAssetHandler)}.PreCommit", request.IdempotencyKey, cancellationToken);

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

        // Ticket 3 (P1) final gap item 5: no-op in production. Lets an integration test simulate a
        // 500/502/503/504 (or a dropped response) occurring AFTER this asset row, the idempotency
        // record and the audit outbox entry have already committed — the exact scenario a retry with
        // the same Idempotency-Key must replay rather than repeat (and, for automatic numbering,
        // must not consume a second asset number).
        await _postCommitFaultInjector.MaybeFailAfterCommitAsync(
            nameof(CreateAssetHandler), request.IdempotencyKey, cancellationToken);

        // Deliver the just-committed audit outbox entry immediately rather than waiting for the
        // next IdempotencyMaintenanceJob cron tick (every 5 minutes). The outbox row remains the
        // source of truth: if this inline attempt throws, the background job still retries it.
        if (auditPublisher is not null && logger is not null)
        {
            try
            {
                await db.DispatchPendingAsync(
                    db.AuditOutboxEntries, auditPublisher, now, IdempotencyCleanupExtensions.DefaultBatchSize,
                    logger, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception,
                    "Inline dispatch of asset-creation outbox entries failed; the background maintenance job will retry.");
            }
        }

        return Result.Success(response);
    }
}
