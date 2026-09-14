using HR.Modules.Sickness.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Features.DeactivateSicknessCategory;

internal sealed class DeactivateSicknessCategoryHandler(SicknessDbContext db, IClock clock, IAuditEventPublisher auditPublisher)
{
    public async Task<Result> HandleAsync(
        DeactivateSicknessCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, object?>(
                scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success();
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var category = await db.SicknessCategories
            .FirstOrDefaultAsync(c => c.Id == request.Id && c.CompanyId == request.CompanyId, cancellationToken);

        if (category is null)
            return Result.Failure(Error.NotFound("Sickness category not found."));

        var now = new DateTimeOffset(clock.UtcNow, TimeSpan.Zero);
        category.Deactivate(now);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await db.SaveIdempotentAsync(
                db.IdempotencyRecords, scope, key, fingerprint!, StatusCodes.Status204NoContent, (object?)null, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success();
        }
        else
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        await auditPublisher.PublishAsync(new SicknessCategoryDeactivatedAuditEvent(
            category.CompanyId,
            category.Id,
            request.ActorEmployeeId,
            category.Name,
            now), cancellationToken);

        return Result.Success();
    }
}
