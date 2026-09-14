using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.CreateEmploymentType;

internal sealed class CreateEmploymentTypeHandler(EmployeesDbContext db, IClock clock)
{
    public async Task<Result<CreateEmploymentTypeResponse>> HandleAsync(
        CreateEmploymentTypeRequest request,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, CreateEmploymentTypeResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<CreateEmploymentTypeResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var exists = await db.EmploymentTypes.AnyAsync(
            e => e.CompanyId == request.CompanyId && e.Name.ToLower() == request.Name.Trim().ToLower(),
            cancellationToken);

        if (exists)
            return Result.Failure<CreateEmploymentTypeResponse>(
                Error.Conflict($"An employment type named '{request.Name}' already exists."));

        var now = new DateTimeOffset(clock.UtcNow, TimeSpan.Zero);
        var entity = EmploymentType.Create(Guid.NewGuid(), request.CompanyId, request.Name, request.Description, now);

        db.EmploymentTypes.Add(entity);

        var response = new CreateEmploymentTypeResponse(
            entity.Id, entity.CompanyId, entity.Name, entity.Description,
            entity.IsActive, entity.CreatedAt, entity.UpdatedAt);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await db.SaveIdempotentAsync<IdempotencyRecord, CreateEmploymentTypeResponse>(db.IdempotencyRecords, 
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
