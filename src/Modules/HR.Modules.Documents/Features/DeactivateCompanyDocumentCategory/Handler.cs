using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.DeactivateCompanyDocumentCategory;

internal sealed class DeactivateCompanyDocumentCategoryHandler(DocumentsDbContext db, IClock clock)
{
    public async Task<Result> HandleAsync(
        DeactivateCompanyDocumentCategoryRequest request,
        CancellationToken cancellationToken)
    {
        // Ticket 3 (P1) follow-up: dedupe a retried/duplicated request. Payload-less success, so a
        // trivial `bool` marker is persisted/replayed purely to detect a repeated delivery.
                var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);
        

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, bool>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success();
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var category = await db.CompanyDocumentCategories
            .SingleOrDefaultAsync(
                c => c.Id == request.CategoryId &&
                     c.CompanyId == request.CompanyId &&
                     c.IsActive,
                cancellationToken);

        if (category is null)
            return Result.Failure(Error.NotFound($"Document category '{request.CategoryId}' was not found."));

        var now = clock.UtcNowOffset();
        category.Deactivate(now);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await db.SaveIdempotentAsync(db.IdempotencyRecords,
            scope, key, fingerprint!, StatusCodes.Status204NoContent, true, now, cancellationToken);

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
