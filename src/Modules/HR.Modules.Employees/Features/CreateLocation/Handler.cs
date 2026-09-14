using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.CreateLocation;

internal sealed class CreateLocationHandler
{
    private readonly EmployeesDbContext _dbContext;
    private readonly IClock _clock;

    public CreateLocationHandler(EmployeesDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<Result<CreateLocationResponse>> HandleAsync(
        CreateLocationRequest request,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await _dbContext.TryReplayAsync<IdempotencyRecord, CreateLocationResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<CreateLocationResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var locationTypeExists = await _dbContext.LocationTypes
            .AnyAsync(
                t => t.Id == request.LocationTypeId &&
                     t.CompanyId == request.CompanyId &&
                     t.IsActive,
                cancellationToken);

        if (!locationTypeExists)
        {
            return Result.Failure<CreateLocationResponse>(
                Error.NotFound($"Location type '{request.LocationTypeId}' was not found."));
        }

        var nameExists = await _dbContext.Locations
            .AnyAsync(
                l => l.CompanyId == request.CompanyId &&
                     l.Name.ToLower() == request.Name.Trim().ToLower() &&
                     l.IsActive,
                cancellationToken);

        if (nameExists)
        {
            return Result.Failure<CreateLocationResponse>(
                Error.Conflict($"An active location named '{request.Name.Trim()}' already exists in this company."));
        }

        var now = _clock.UtcNowOffset();

        var location = Location.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.LocationTypeId,
            request.Name.Trim(),
            string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            now);

        _dbContext.Locations.Add(location);

        var response = new CreateLocationResponse(
            location.Id,
            location.CompanyId,
            location.Name,
            location.Description,
            location.LocationTypeId,
            location.IsActive,
            location.CreatedAt);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await _dbContext.SaveIdempotentAsync<IdempotencyRecord, CreateLocationResponse>(_dbContext.IdempotencyRecords, 
                scope, key, fingerprint!, StatusCodes.Status201Created, response, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(response);
    }
}
