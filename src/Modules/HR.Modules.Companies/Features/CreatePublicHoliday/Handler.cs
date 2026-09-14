using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Features.CreatePublicHoliday;

internal sealed class CreatePublicHolidayHandler
{
    private readonly CompaniesDbContext _dbContext;
    private readonly IClock _clock;

    public CreatePublicHolidayHandler(CompaniesDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<Result<CreatePublicHolidayResponse>> HandleAsync(
        CreatePublicHolidayRequest request,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await _dbContext.TryReplayAsync<IdempotencyRecord, CreatePublicHolidayResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<CreatePublicHolidayResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var exists = await _dbContext.PublicHolidays
            .AnyAsync(
                h => h.CompanyId == request.CompanyId && h.Date == request.Date,
                cancellationToken);

        if (exists)
        {
            return Result.Failure<CreatePublicHolidayResponse>(
                Error.Conflict($"A public holiday on {request.Date} already exists for this company."));
        }

        var now = _clock.UtcNowOffset();

        var holiday = PublicHoliday.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.Date,
            request.Name.Trim(),
            request.CountryCode.Trim().ToUpperInvariant(),
            now);

        _dbContext.PublicHolidays.Add(holiday);

        var response = new CreatePublicHolidayResponse(
            holiday.Id,
            holiday.CompanyId,
            holiday.Date,
            holiday.Name,
            holiday.CountryCode,
            holiday.CreatedAt);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await _dbContext.SaveIdempotentAsync(
                _dbContext.IdempotencyRecords, scope, key, fingerprint!, StatusCodes.Status201Created, response, now, cancellationToken);

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
