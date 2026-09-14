using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.SetEmployeeWorkingPattern;

internal sealed class SetEmployeeWorkingPatternHandler(EmployeesDbContext dbContext, IClock clock)
{
    public async Task<Result<SetEmployeeWorkingPatternResponse>> HandleAsync(
        SetEmployeeWorkingPatternRequest request,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await dbContext.TryReplayAsync<IdempotencyRecord, SetEmployeeWorkingPatternResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<SetEmployeeWorkingPatternResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var employee = await dbContext.Employees
            .SingleOrDefaultAsync(
                e => e.Id == request.EmployeeId && e.CompanyId == request.CompanyId,
                cancellationToken);

        if (employee is null)
            return Result.Failure<SetEmployeeWorkingPatternResponse>(
                Error.NotFound($"Employee '{request.EmployeeId}' was not found."));

        var now = clock.UtcNowOffset();
        employee.SetWorkingPattern(request.WorkingDaysOverride, request.HoursPerDayOverride, now);

        var response = new SetEmployeeWorkingPatternResponse(
            employee.Id,
            employee.CompanyId,
            employee.WorkingDaysOverride,
            employee.HoursPerDayOverride,
            employee.UpdatedAt);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await dbContext.SaveIdempotentAsync<IdempotencyRecord, SetEmployeeWorkingPatternResponse>(dbContext.IdempotencyRecords, 
                scope, key, fingerprint!, StatusCodes.Status200OK, response, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(response);
    }
}
