using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Features.CreateSicknessCategory;

internal sealed class CreateSicknessCategoryHandler(SicknessDbContext db, IClock clock, IAuditEventPublisher auditPublisher)
{
    public async Task<Result<CreateSicknessCategoryResponse>> HandleAsync(
        CreateSicknessCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, CreateSicknessCategoryResponse>(
                scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<CreateSicknessCategoryResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var exists = await db.SicknessCategories
            .AnyAsync(c => c.CompanyId == request.CompanyId && c.Name.ToLower() == request.Name.Trim().ToLower(), cancellationToken);

        if (exists)
            return Result.Failure<CreateSicknessCategoryResponse>(Error.Conflict("A sickness category with this name already exists."));

        var now = new DateTimeOffset(clock.UtcNow, TimeSpan.Zero);
        var entity = SicknessCategory.Create(Guid.NewGuid(), request.CompanyId, request.Name, request.DisplayOrder, now);

        db.SicknessCategories.Add(entity);

        var response = new CreateSicknessCategoryResponse(
            entity.Id, entity.CompanyId, entity.Name, entity.IsActive, entity.DisplayOrder, entity.CreatedAt, entity.UpdatedAt);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await db.SaveIdempotentAsync(
                db.IdempotencyRecords, scope, key, fingerprint!, StatusCodes.Status201Created, response, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        await auditPublisher.PublishAsync(new SicknessCategoryCreatedAuditEvent(
            entity.CompanyId,
            entity.Id,
            request.ActorEmployeeId,
            entity.Name,
            entity.DisplayOrder,
            now), cancellationToken);

        return Result.Success(response);
    }
}
