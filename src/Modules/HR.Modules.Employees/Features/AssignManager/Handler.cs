using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.AssignManager;

internal sealed class AssignManagerHandler
{
    private readonly EmployeesDbContext _dbContext;
    private readonly IClock _clock;
    private readonly IIntegrationEventPublisher _integrationEventPublisher;

    public AssignManagerHandler(EmployeesDbContext dbContext, IClock clock, IIntegrationEventPublisher integrationEventPublisher)
    {
        _dbContext = dbContext;
        _clock = clock;
        _integrationEventPublisher = integrationEventPublisher;
    }

    public async Task<Result<AssignManagerResponse>> HandleAsync(
        AssignManagerRequest request,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await _dbContext.TryReplayAsync<IdempotencyRecord, AssignManagerResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<AssignManagerResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var employee = await _dbContext.Employees
            .SingleOrDefaultAsync(
                e => e.Id == request.Id && e.CompanyId == request.CompanyId,
                cancellationToken);

        if (employee is null)
        {
            return Result.Failure<AssignManagerResponse>(
                Error.NotFound($"Employee with id '{request.Id}' was not found."));
        }

        string? managerFullName = null;

        if (request.ManagerId is not null)
        {
            var manager = await _dbContext.Employees
                .SingleOrDefaultAsync(
                    e => e.Id == request.ManagerId &&
                         e.CompanyId == request.CompanyId &&
                         e.Status != EmploymentStatus.FormerEmployee,
                    cancellationToken);

            if (manager is null)
            {
                return Result.Failure<AssignManagerResponse>(
                    Error.NotFound($"Manager employee '{request.ManagerId}' was not found."));
            }

            // Circular hierarchy check: walk up the proposed manager's chain.
            // If we reach the employee being updated, the assignment would create a cycle.
            var allEmployees = await _dbContext.Employees
                .AsNoTracking()
                .Where(e => e.CompanyId == request.CompanyId)
                .Select(e => new { e.Id, e.ManagerId })
                .ToDictionaryAsync(e => e.Id, e => e.ManagerId, cancellationToken);

            var visited = new HashSet<Guid>();
            var cursor = request.ManagerId;

            while (cursor is not null)
            {
                if (cursor == request.Id)
                {
                    return Result.Failure<AssignManagerResponse>(
                        Error.Conflict("This assignment would create a circular management hierarchy."));
                }

                if (!visited.Add(cursor.Value))
                    break; // Existing cycle in data — stop to avoid infinite loop

                cursor = allEmployees.TryGetValue(cursor.Value, out var nextManagerId) ? nextManagerId : null;
            }

            managerFullName = $"{manager.FirstName} {manager.LastName}";
        }

        var now = _clock.UtcNowOffset();
        var previousManagerId = employee.ManagerId;

        employee.Assign(employee.DepartmentId, employee.PositionProfileId, employee.LocationId, request.ManagerId, now);

        var response = new AssignManagerResponse(
            employee.Id,
            employee.CompanyId,
            employee.ManagerId,
            managerFullName,
            employee.UpdatedAt);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await _dbContext.SaveIdempotentAsync<IdempotencyRecord, AssignManagerResponse>(_dbContext.IdempotencyRecords, 
                scope, key, fingerprint!, StatusCodes.Status200OK, response, now, cancellationToken);

            // Lost a race against a concurrent duplicate under the same key - this attempt's
            // assignment was rolled back along with it, so skip our own post-save side effects and
            // hand back the winner's result untouched.
            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        if (previousManagerId != employee.ManagerId)
        {
            await _integrationEventPublisher.PublishAsync(
                new EmployeeManagerChangedIntegrationEvent(
                    employee.CompanyId, employee.Id, previousManagerId, employee.ManagerId, now),
                cancellationToken);
        }

        return Result.Success(response);
    }
}
